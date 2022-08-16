using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Linguistic;
using System.Globalization;

namespace Phoenix.Bot.Bots
{
    public class DialogBot<TDialog> : ActivityHandler
        where TDialog : Dialog
    {
        private readonly UserState _userState;
        private readonly ConversationState _convState;
        private readonly IStatePropertyAccessor<UserData> _userDataAcsr;
        private readonly IStatePropertyAccessor<ConversationData> _convDataAcsr;

        private readonly ApplicationUserManager _userManager;

        private readonly BotDataContext _botDataContext;
        private readonly UserRepository _userRepository;
        private readonly SchoolConnectionRepository _schoolConnectionRepository;
        private readonly UserConnectionRepository _userConnectionRepository;
        
        private readonly IConfiguration _configuration;

        protected readonly Dialog Dialog;

        public DialogBot(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,
            BotDataContext botDataContext,
            IConfiguration configuration,
            TDialog dialog)
        {
            _userState = userState;
            _convState = convState;
            _userDataAcsr = userState.CreateProperty<UserData>(nameof(UserData));
            _convDataAcsr = convState.CreateProperty<ConversationData>(nameof(ConversationData));

            _userManager = userManager;

            _botDataContext = botDataContext;
            _userRepository = new(phoenixContext);
            _schoolConnectionRepository = new(phoenixContext);
            _userConnectionRepository = new(phoenixContext);
            
            _configuration = configuration;

            this.Dialog = dialog;
        }

        public override async Task OnTurnAsync(ITurnContext turnCtx,
        CancellationToken canTkn = default)
        {
            await turnCtx.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, canTkn);

            var userData = await _userDataAcsr.GetAsync(turnCtx, () => new(), canTkn);
            var convData = await _convDataAcsr.GetAsync(turnCtx, () => new(), canTkn);

            // Check if school connection exists and is active
            var schoolConnection = await _schoolConnectionRepository.FindUniqueAsync(
                turnCtx.GetProvider(), turnCtx.GetRecipientKey(), canTkn);

            if (schoolConnection is null)
                throw new BotException(BotError.SchoolNotFound);

            if (!schoolConnection.ActivatedAt.HasValue)
                throw new BotException(BotError.SchoolNotConnected);

            convData.Refresh(schoolConnection);
            await _convDataAcsr.SetAsync(turnCtx, convData, canTkn);

            // Check if a user is connected and if their connection is active
            var userConnection = await _userConnectionRepository.FindUniqueAsync(
                turnCtx.GetProvider(), turnCtx.GetProviderKey(), canTkn);

            await userData.RefreshAsync(userConnection, _userManager);

            if (userData.IsConnected)
            {
                var userRoles = await _userManager.GetRoleRanksAsync(userData.AppUser!);
                if (!userData.PhoenixUser!.Schools.Contains(convData.School) && !userRoles.Any(rr => rr.IsSuper()))
                {
                    userData.IsConnected = false;

                    throw new BotException(BotError.UserNotSubscribedToSchool);
                }

                if (userData.SelectedRole.HasValue && !userRoles.Contains(userData.SelectedRole.Value))
                {
                    userData.SelectedRole = null;
                    await _convState.DeleteAsync(turnCtx, canTkn);
                }
            }

            await _userDataAcsr.SetAsync(turnCtx, userData, canTkn);

            // Execute Bot's turn
            await base.OnTurnAsync(turnCtx, canTkn);

            // Save any state changes that might have occured during the turn
            await _convState.SaveChangesAsync(turnCtx, force: false, canTkn);
            await _userState.SaveChangesAsync(turnCtx, force: false, canTkn);
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnCtx,
            CancellationToken canTkn = default)
        {
            var userData = await _userDataAcsr.GetAsync(turnCtx, () => new(), canTkn);
            var convData = await _convDataAcsr.GetAsync(turnCtx, () => new(), canTkn);

            var schoolConnection = await _schoolConnectionRepository.FindUniqueAsync(
                turnCtx.GetProvider(), turnCtx.GetRecipientKey(), canTkn);

            // Handle command
            convData.Command = await HandleCommandAsync(turnCtx, canTkn);

            // Determine locale
            string locale;
            if (schoolConnection is null || userData.IsBackend)
                locale = "en-US";
            else
            {
                if (userData.SelectedRole.HasValue)
                    locale = userData.SelectedRole.Value == RoleRank.Parent
                        ? schoolConnection.Tenant.SchoolSetting.SecondaryLocale
                        : schoolConnection.Tenant.SchoolSetting.PrimaryLocale;
                else
                    locale = schoolConnection.Tenant.SchoolSetting.PrimaryLocale;
            }

            await _convDataAcsr.SetAsync(turnCtx, convData, canTkn);

            // Set threads locale
            // TODO: Check which to keep
            
            //Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture(locale);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CreateSpecificCulture(locale!);

            // Run Main Dialog
            await Dialog.RunAsync(turnCtx, _convState.CreateProperty<DialogState>(nameof(DialogState)), canTkn);
        }

        private async Task<Command> HandleCommandAsync(ITurnContext<IMessageActivity> turnCtx,
            CancellationToken canTkn = default)
        {
            Command cmd = turnCtx.Activity.Text.InferCommand();

            if (cmd != Command.NoCommand)
            {
                // Reset the dialog state
                await _convDataAcsr.DeleteAsync(turnCtx, canTkn);

                switch (cmd)
                {
                    case Command.GetStarted:
                    case Command.Greeting:
                        await GreetAsync(turnCtx);
                        goto case Command.Reset;

                    case Command.Reset:
                        await _convState.DeleteAsync(turnCtx, canTkn);
                        break;

                    case Command.Logout:
                        await LogoutAsync(turnCtx, canTkn);
                        break;
                }
            }

            return cmd;
        }

        private async Task GreetAsync(ITurnContext<IMessageActivity> turnCtx)
        {
            var gifUrl = await DialogsHelper.FindGifUrlAsync(
                            "g", "hi", 10, new Random().Next(10), _configuration["GiphyKey"]);

            var reply = MessageFactory.ContentUrl(
                url: gifUrl, contentType: "image/gif", text: "Γεια σου!! 😊");

            await turnCtx.SendActivityAsync(reply);
        }

        private async Task LogoutAsync(ITurnContext<IMessageActivity> turnCtx,
            CancellationToken canTkn = default)
        {
            var userConnection = await _userConnectionRepository.FindUniqueAsync(
                            turnCtx.GetProvider(), turnCtx.GetProviderKey(), canTkn);

            if (userConnection is null || !userConnection.ActivatedAt.HasValue)
                return;

            // Disconnect affiliated from channel
            var affiliatedConnections = await _userConnectionRepository.DisconnectAffiliatedAsync(
                turnCtx.GetProvider(), userConnection.TenantId, canTkn);

            // Disconnect user
            await _userConnectionRepository.DisconnectAsync(userConnection, canTkn);

            // Delete conversation data of user and affiliated users
            List<BotDataEntity> botDataToRemove = new();
            var botConvData = _botDataContext.BotDataEntity.
                Where(bd => bd.RealId.Contains("conversations"));

            foreach (var affConn in affiliatedConnections)
            {
                botDataToRemove.AddRange(botConvData
                    .Where(bd => bd.RealId.Contains(affConn.Channel))
                    .Where(bd => bd.RealId.Contains(affConn.ChannelKey)));
            }

            _botDataContext.RemoveRange(botDataToRemove);
            _botDataContext.SaveChanges();

            await _convState.ClearStateAsync(turnCtx, canTkn);
            await _userState.ClearStateAsync(turnCtx, canTkn);
        }
    }
}

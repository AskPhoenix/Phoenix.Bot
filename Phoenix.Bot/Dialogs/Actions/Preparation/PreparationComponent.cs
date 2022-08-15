using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public abstract class PreparationComponent : StateDialog
    {
        protected abstract string AskPrompt { get; }
        protected abstract string AskRePrompt { get; }

        protected PreparationComponent(
            BotActionPreparation preparation,
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, preparation.PreparationComponentName())
        {
            string wfName = WaterfallNames.Actions.Preparation.PreparationWaterfallName(preparation);

            AddDialog(new WaterfallDialog(wfName,
                new WaterfallStep[]
                {
                    InitializeStepAsync,
                    AskStepAsync,
                    SelectStepAsync
                }));

            InitialDialogId = wfName;
        }

        protected abstract Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn);
        
        protected virtual async Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;

            if (options.Selectables!.Count == 1)
                return await stepCtx.EndDialogAsync(options.Selectables.Single().Key, canTkn);

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(this.AskPrompt),
                    RetryPrompt = MessageFactory.Text(this.AskRePrompt),
                    Choices = ChoiceFactory.ToChoices(options.Selectables!.Values.ToList())
                }, canTkn);
        }

        protected virtual async Task<DialogTurnResult> SelectStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;
            var selectedIndex = ((FoundChoice)stepCtx.Result).Index;

            return await stepCtx.EndDialogAsync(options.Selectables?.ElementAt(selectedIndex).Key, canTkn);
        }

        #region User Preparation Initialization

        protected bool IsSelfPreparation(PreparationComponentOptions options) =>
            !options.UserIdToPrepareFor.HasValue ||
             options.UserIdToPrepareFor.Value == UData.PhoenixUser!.AspNetUserId;

        protected virtual async Task<User> FindUserToPrepareForAsync(PreparationComponentOptions options)
        {
            if (IsSelfPreparation(options))
                return UData.PhoenixUser!;

            return (await _userRepository.FindPrimaryAsync(options.UserIdToPrepareFor!.Value))
                ?? throw new BotException(BotError.UserNotValid);
        }

        #endregion

        #region Selectables

        internal static Dictionary<int, string> GetSelectables(IEnumerable<User> users)
        {
            if (users is null)
                throw new ArgumentNullException(nameof(users));

            return users.ToDictionary(u => u.AspNetUserId, u => u.FirstName);
        }

        internal static Dictionary<int, string> GetSelectables(IEnumerable<Course> courses,
            bool showByGroup = false)
        {
            if (courses is null)
                throw new ArgumentNullException(nameof(courses));

            static string elementSelector(Course c) =>
                c.Name + (string.IsNullOrEmpty(c.SubCourse) ? "" : $" - " + c.SubCourse);

            if (showByGroup)
                return courses.ToDictionary(c => c.Id, c => elementSelector(c) + " ~ " + c.Group);

            return courses.ToDictionary(c => c.Id, elementSelector);
        }

        internal static Dictionary<int, string> GetSelectables(IEnumerable<DateTimeOffset> dates,
            string dateFormat = "d/M")
        {
            if (dates is null)
                throw new ArgumentNullException(nameof(dates));

            return new(dates.Select((d, i) => new KeyValuePair<int, string>(i, d.ToString(dateFormat))));
        }

        internal static Dictionary<int, string> GetSelectables(IEnumerable<Lecture> lectures)
        {
            if (lectures is null)
                throw new ArgumentNullException(nameof(lectures));

            return lectures.ToDictionary(l => l.Id, l => l.StartDateTime.ToString("t"));
        }

        #endregion
    }
}

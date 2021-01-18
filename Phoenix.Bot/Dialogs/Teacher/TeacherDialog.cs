using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Channels.Facebook.FacebookEvents;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;

namespace Phoenix.Bot.Dialogs.Teacher
{
    public class TeacherDialog : ComponentDialog
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly AspNetUserRepository userRepository;
        private readonly ApplicationStore appStore;

        private static class WaterfallNames
        {
            public const string Menu = "Teacher_Menu_WaterfallDialog";
            public const string Help = "Teacher_Help_WaterfallDialog";
        }

        public TeacherDialog(UserManager<ApplicationUser> userManager, PhoenixContext phoenixContext, ApplicationDbContext appContext)
            : base(nameof(TeacherDialog))
        {
            this.userManager = userManager;
            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.appStore = new ApplicationStore(appContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new WaterfallDialog(WaterfallNames.Menu,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    TaskStepAsync,
                    LoopStepAsync
                }));

            InitialDialogId = WaterfallNames.Menu;
        }

        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            LoginProvider provider = innerDc.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = innerDc.Context.Activity.From.Id;
            var appUser = await appStore.FindByProviderKeyAsync(provider, providerKey);

            if (appUser != null && !await userManager.HasPasswordAsync(appUser))
            {
                string password = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Substring(0, 8);
                await userManager.AddPasswordAsync(appUser, password);

                await innerDc.Context.SendActivityAsync("Ο παρακάτω κωδικός θα χρειαστεί κατά την πρώτη σύνδεση, ενώ στη συνέχεια μπορείς να τον αλλάξεις:");
                await innerDc.Context.SendActivityAsync(password);
            }

            //TODO: Set PhoneNumberConfirmed during authentication
            var user = userRepository.FindUserFromLogin(provider, providerKey);
            user.PhoneNumberConfirmed = true;
            userRepository.Update(user);

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Teacher Menu Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Options is int index)
                return await stepContext.NextAsync(
                    new FoundChoice()
                    {
                        Index = index,
                        Value = index switch { 0 => "Εργασίες", 1 => "Διαγωνίσματα", 2 => /*"Βαθμολογίες", 3 =>*/ "Πρόγραμμα", _ => string.Empty },
                        Score = 1.0f
                    },
                    cancellationToken);

            //TODO: For the schedule, only roles > Teacher should be able to edit it. Teachers will only have the permission to view it
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς θα μπορούσα να σε βοηθήσω;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω απαντήσεις:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "📚 Εργασίες", "📝 Διαγωνίσματα", /*"💯 Βαθμολογίες", */"📅 Πρόγραμμα" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> TaskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            
            string taskArticle = string.Empty;
            string taskName = foundChoice.Value.TrimEmojis().ToLower();
            string imageUrl = "https://www.bot.askphoenix.gr/assets/";
            var button = new UrlButton(title: foundChoice.Value + "...", url: "https://pwa.askphoenix.gr/",
                webviewHeightRatio: "tall", messengerExtensions: true);

            switch (foundChoice.Index)
            {
                case 0:
                    taskArticle = "τις";
                    button.Url += "teacher/homework";
                    imageUrl += "exercise_bg.png";
                    break;
                case 1:
                    taskArticle = "τα";
                    button.Url += "teacher/exams";
                    imageUrl += "exam_bg.png";
                    break;
                //case 2:
                //    taskArticle = "τις";
                //    button.Url += "teacher/gradation";
                //    imageUrl += "gradation_bg.png";
                //    break;
                case 2:
                    taskArticle = "το";
                    button.Url += "teacher/schedule";
                    imageUrl += "schedule_bg.png";
                    break;
            }

            LoginProvider provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            string providerKey = stepContext.Context.Activity.From.Id;

            string signature = userRepository.FindUserFromLogin(provider, providerKey).GetHashSignature();
            signature = WebUtility.UrlEncode(signature);
            
            button.Url += $"?signature={signature}&t={DateTime.Now:yyyyMMddHHmmss}";

            var taskCard = new GenericTemplate()
            {
                ImageAspectRatio = "square",
                Elements = new GenericElement[1]
                {
                    new GenericElement()
                    {
                        Title = $"Μετάβαση σ{taskArticle} {taskName}" + (foundChoice.Index == 3 ? " διδασκαλίας" : ""),
                        Subtitle = $"Προβολή ή επεξεργασία των πληροφοριών σχετικά με {taskArticle} {taskName}.",
                        ImageUrl = imageUrl,
                        DefaultAction = button.ToUrlAction(),
                        Buttons = new Button[] { button }
                    }
                }
            };

            var reply = MessageFactory.SuggestedActions(new string[1] { "🔙 Επιστροφή" });
            reply.ChannelData = ChannelDataFactory.Template(taskCard);
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return new DialogTurnResult(DialogTurnStatus.Waiting);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);

        #endregion
    }
}

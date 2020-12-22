using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Channels.Facebook.FacebookEvents;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.DataHandle.Main.Models;

namespace Phoenix.Bot.Dialogs.Teacher
{
    public class TeacherDialog : ComponentDialog
    {
        private readonly PhoenixContext _phoenixContext;

        private static class WaterfallNames
        {
            public const string Menu = "Teacher_Menu_WaterfallDialog";
            public const string Help = "Teacher_Help_WaterfallDialog";
        }

        public TeacherDialog(PhoenixContext phoenixContext)
            : base(nameof(TeacherDialog))
        {
            _phoenixContext = phoenixContext;

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

            string signature = _phoenixContext.AspNetUsers.Single(u => u.AspNetUserLogins.Any(l => l.ProviderKey == stepContext.Context.Activity.From.Id && l.UserId == u.Id)).GetHashSignature();
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

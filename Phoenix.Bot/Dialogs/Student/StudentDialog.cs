using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Common;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Channels.Facebook.FacebookEvents;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Student
{
    public class StudentDialog : ComponentDialog
    {
        public StudentDialog(FeedbackDialog feedbackDialog, ExerciseDialog exerciseDialog, ExamDialog examDialog, ScheduleDialog scheduleDialog)
            : base(nameof(StudentDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(feedbackDialog);
            AddDialog(exerciseDialog);
            AddDialog(examDialog);
            AddDialog(scheduleDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Student.Home,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    ActionStepAsync,
                    FeedbackStepAsync,
                    LoopStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Student.Help,
                new WaterfallStep[] 
                {
                    HelpTopicsStepAsync,
                    TopicRedirectStepAsync,
                    AfterTopicStepAsync,
                    FinalHelpStepAsync
                }));

            InitialDialogId = WaterfallNames.Student.Home;
        }

        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            if ((options as StudentOptions).Action == StudentAction.Help)
                InitialDialogId = WaterfallNames.Student.Help;

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Home Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            if (studentOptions.Action != StudentAction.NoAction)
                return await stepContext.NextAsync(null, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς θα μπορούσα να σε βοηθήσω;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω απαντήσεις:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "📚 Εργασίες", "📝 Διαγωνίσματα", "📅 Πρόγραμμα", "💪 Βοήθεια", "👍 Κάνε ένα σχόλιο" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> ActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            if (stepContext.Result is FoundChoice foundChoice)
                studentOptions.Action = (StudentAction)(foundChoice.Index + 1);

            switch (studentOptions.Action)
            {
                case StudentAction.Exercises:
                    return await stepContext.BeginDialogAsync(nameof(ExerciseDialog), null, cancellationToken);
                case StudentAction.Exams:
                    return await stepContext.BeginDialogAsync(nameof(ExamDialog), null, cancellationToken);
                case StudentAction.Schedule:
                    return await stepContext.BeginDialogAsync(nameof(ScheduleDialog), null, cancellationToken);
                case StudentAction.Help:
                    return await stepContext.BeginDialogAsync(WaterfallNames.Student.Help, null, cancellationToken);
                case StudentAction.Feedback:
                    var feedbackOptions = new FeedbackOptions()
                    {
                        BotAskedForFeedback = false,
                        UserId = studentOptions.StudentId
                    };
                    return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
                default:
                    return await stepContext.EndDialogAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> FeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            if (studentOptions.Action != StudentAction.Feedback && new Random().Next(3) == 0)
            {
                var feedbackOptions = new FeedbackOptions() 
                {
                    BotAskedForFeedback = true,
                    UserId = studentOptions.StudentId
                };
                return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            studentOptions.Action = StudentAction.NoAction;

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, studentOptions, cancellationToken);
        }

        #endregion

        #region Help Waterfall Dialog

        private async Task<DialogTurnResult> HelpTopicsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var topics = new GenericTemplate()
            {
                ImageAspectRatio = "square",
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "📚 Εργασίες",
                        Subtitle = "Μάθε πώς μπορείς να διαχειριστείς τις εργασίες για το σπίτι.",
                        ImageUrl = "https://www.bot.askphoenix.gr/assets/exercise_bg.png",
                        Buttons = new Button[] { new PostbackButton("🦜 Περισσότερα", "Περισσότερα για τις εργασίες") }
                    },
                    new GenericElement()
                    {
                        Title = "📝 Διαγωνίσματα",
                        Subtitle = "Μάθε πώς μπορείς να διαχειριστείς τα διαγωνίσματά σου.",
                        ImageUrl = "https://www.bot.askphoenix.gr/assets/exam_bg.png",
                        Buttons = new Button[] { new PostbackButton("🕊 Περισσότερα", "Περισσότερα για τα διαγωνίσματα") }
                    },
                    new GenericElement()
                    {
                        Title = "📅 Πρόγραμμα",
                        Subtitle = "Μάθε πώς μπορείς να δεις το πρόγραμμα των μαθημάτων σου.",
                        ImageUrl = "https://www.bot.askphoenix.gr/assets/schedule_bg.png",
                        Buttons = new Button[] { new PostbackButton("🐧 Περισσότερα", "Περισσότερα για το πρόγραμμα") }
                    }
                }
            };

            var reply = MessageFactory.Text("Σε ποιο από τα παρακάτω θέματα θα ήθελες βοήθεια;");
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(topics);
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Style = ListStyle.None,
                    Choices = ChoiceFactory.ToChoices(new string[] { "Περισσότερα για τις εργασίες", "Περισσότερα για τα διαγωνίσματα", "Περισσότερα για το πρόγραμμα" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> TopicRedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text("Ελπίζω οι συμβουλές που ακολουθούν να σου φανούν χρήσιμες! 😊");
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            reply.Text = "Μπορείς να μεταβείς στην αντίστοιχη ενότητα, πατώντας στο κουμπί \"Άνοιγμα\" παρακάτω:";
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return (stepContext.Result as FoundChoice).Index switch
            {
                0 => await ExerciseHelpStepAsync(stepContext, cancellationToken),
                1 => await ExamHelpStepAsync(stepContext, cancellationToken),
                2 => await ScheduleHelpStepAsync(stepContext, cancellationToken),
                _ => await stepContext.EndDialogAsync(null, cancellationToken)
            };
        }

        private async Task<DialogTurnResult> ExerciseHelpStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var exerciseCards = new GenericTemplate()
            {
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "📚 Eργασίες",
                        Subtitle = "Μάθε τη δουλειά για το σπίτι για κάποιο μάθημα.",
                        Buttons = new Button[] { new PostbackButton("🎬 Άνοιγμα εργασιών") }
                    },
                    new GenericElement()
                    {
                        Title = "⏭ Για το επόμενο μάθημα",
                        Subtitle = "Δες τις εργασίες για το σπίτι που έχεις για το επόμενο μάθημα."
                    },
                    new GenericElement()
                    {
                        Title = "⌚ Για άλλο μάθημα",
                        Subtitle = "Δες παλαιότερες εργασίες ή αν υπάρχει κάτι για κάποιο μελλοντικό μάθημα."
                    }
                }
            };

            var reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(exerciseCards);
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ExamHelpStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var examCards = new GenericTemplate()
            {
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "📝 Διαγωνίσματα",
                        Subtitle = "Δες πληροφορίες για τα διαγωνίσματά σου.",
                        Buttons = new Button[] { new PostbackButton("🎬 Άνοιγμα διαγωνισμάτων") }
                    },
                    new GenericElement()
                    {
                        Title = "💯 Επιδόσεις",
                        Subtitle = "Έλεγξε τη βαθμολογία σου στα διαγωνίσματα που έχεις γράψει."
                    },
                    new GenericElement()
                    {
                        Title = "🔮 Μελλοντικά",
                        Subtitle = "Μάθε πότε είναι τα επόμενα διαγωνίσματα.",
                    },
                    new GenericElement()
                    {
                        Title = "🏃 Προετοιμασία",
                        Subtitle = "Δες την ύλη και τι πρέπει να διαβάσεις για το επόμενο διαγώνισμα."
                    }
                }
            };

            var reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(examCards);
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> ScheduleHelpStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var scheduleCards = new GenericTemplate()
            {
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "📅 Πρόγραμμα",
                        Subtitle = "Μάθε πληροφορίες για το πρόγραμμα των μαθημάτων σου.",
                        Buttons = new Button[] { new PostbackButton("🎬 Άνοιγμα προγράμματος") }
                    },
                    new GenericElement()
                    {
                        Title = "🌞 Σημερινό",
                        Subtitle = "Δες τις ώρες και τις αίθουσες για τα μαθήματα που έχεις για σήμερα."
                    },
                    new GenericElement()
                    {
                        Title = "🌅 Άλλη μέρα",
                        Subtitle = "Δες το πρόγραμμα των μαθημάτων για μια μέρα της επιλογής σου."
                    },
                    new GenericElement()
                    {
                        Title = "🗓 Εβδομαδιαίο",
                        Subtitle = "Δες το πρόγραμμα των μαθημάτων σου για την επόμενη εβδομάδα."
                    },
                    new GenericElement()
                    {
                        Title = "🔄 Αλλαγές",
                        Subtitle = "Οι αλλαγές στο πρόγραμμα εμφανίζονται αυτόματα στις αντίστοιχες ημέρες και ώρες."
                    }
                }
            };

            var reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(scheduleCards);
            await stepContext.Context.SendActivityAsync(reply, cancellationToken);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> AfterTopicStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var promptSuggestedActions = new CardAction[]
            {
                new CardAction(ActionTypes.ImBack, "✔️ Ναι"),
                new CardAction(ActionTypes.ImBack, "❌ Όχι, ευχαριστώ")
            };

            var promptReply = (Activity)MessageFactory.SuggestedActions(promptSuggestedActions, 
                text: "Χρειάζεσαι βοήθεια σε κάποιο άλλο θέμα;");
            var rePromptReply = (Activity)MessageFactory.SuggestedActions(promptSuggestedActions, 
                text: "Παρακαλώ επίλεξε αν χρειάζεσαι βοήθεια σε κάποιο άλλο θέμα, ή όχι για να επιστρέψεις στο αρχικό μενού:");

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = promptReply,
                    RetryPrompt = rePromptReply,
                    Choices = new Choice[] 
                        {
                            new Choice("✔️ Ναι"),
                            new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } },
                            new Choice("🎬 Άνοιγμα εργασιών") { Synonyms = new List<string> { "Άνοιγμα" } },
                            new Choice("🎬 Άνοιγμα διαγωνισμάτων"),
                            new Choice("🎬 Άνοιγμα προγράμματος")
                        },
                    Style = ListStyle.None
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> FinalHelpStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return (stepContext.Result as FoundChoice).Index switch
            {
                0 => await stepContext.ReplaceDialogAsync(WaterfallNames.Student.Help, stepContext.Options, cancellationToken),
                var x when x >= 2 => await stepContext.ReplaceDialogAsync(WaterfallNames.Student.Home, new StudentOptions() { Action = (StudentAction)(x-2) }, cancellationToken),
                _ => await stepContext.EndDialogAsync(null, cancellationToken)
            };
        }

        #endregion
    }

}

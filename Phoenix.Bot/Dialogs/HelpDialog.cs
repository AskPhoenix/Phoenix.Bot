using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options;
using System.Linq;
using Phoenix.DataHandle.Main;
using System;
using Phoenix.Bot.Utilities.Actions;

namespace Phoenix.Bot.Dialogs
{
    public class HelpDialog : ComponentDialog
    {
        private readonly IStatePropertyAccessor<UserData> userDataAccesor;

        public HelpDialog(UserState userState)
            : base(nameof(HelpDialog))
        {
            this.userDataAccesor = userState.CreateProperty<UserData>(nameof(UserData));

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new UnaccentedChoicePrompt(PromptNames.HiddenChoices, PromptValidators.HiddenChoicesValidator));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Ask,
               new WaterfallStep[]
               {
                    HelpAskStepAsync,
                    HelpReplyStepAsync,
               }));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Intro,
                new WaterfallStep[]
                {
                    IntroStepAsync,
                    IntroContinueStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Menu,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    MenuContinueStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Actions,
                new WaterfallStep[]
                {
                    ActionsStepAsync,
                    ActionsContinueStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.Commands,
                new WaterfallStep[]
                {
                    CommandsStepAsync,
                    CommandsContinueStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Help.ActionDetails,
                new WaterfallStep[]
                {
                    ActionDetailsStepAsync,
                    ActionDetailsContinueStepAsync
                }));

            InitialDialogId = WaterfallNames.Help.Intro;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            var helpOptions = options as HelpOptions;
            if (helpOptions.AskForTutorial)
                InitialDialogId = WaterfallNames.Help.Ask;

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        protected override Task OnEndDialogAsync(ITurnContext context, DialogInstance instance, DialogReason reason, CancellationToken cancellationToken = default)
        {
            context.SendActivityAsync("Αν χρειαστείς βοήθεια, μη διστάσεις να ακολουθήσεις την περιήγηση αυτή ξανά!").Wait();
            return base.OnEndDialogAsync(context, instance, reason, cancellationToken);
        }

        #region Ask Waterfall Dialog

        private async Task<DialogTurnResult> HelpAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να σου δείξω τι μπορώ να κάνω με μια σύντομη περιήγηση;"),
                cancellationToken);
        }

        private async Task<DialogTurnResult> HelpReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Τέλεια! 😁 Ας ξεκινήσουμε!"));
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Intro, stepContext.Options, cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Intro Waterfall Dialog

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Αρχικά, ας δούμε τα βασικά:");

            var cards = new HeroCard[3]
            {
                new HeroCard(title: "🤖 Chatbots", subtitle: "Το chatbot είναι μια εφαρμογή συνομιλίας."),
                new HeroCard(title: "🗣 Συνομιλία", subtitle: "Ένα chatbot περιέχει διαλόγους που υλοποιούν μια συγκεκριμένη δυνατότητα."),
                new HeroCard(title: "💬 Μηνύματα", subtitle: "Η επικοινωνία στους διάφορους διαλόγους γίνεται μέσω μηνυμάτων.")
            };

            await stepContext.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(hasPrevious: false),
                cancellationToken);
        }

        private async Task<DialogTurnResult> IntroContinueStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Menu, stepContext.Options, cancellationToken);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Menu Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Τώρα ας δούμε το «αρχικό μενού»:");

            var cards = new HeroCard[3]
            {
                new HeroCard(title: "📜 Αρχικό μενού", subtitle: "Το «αρχικό μενού» περιέχει όλες τις διαθέσιμες δυνατότητες."),
                new HeroCard(title: "🎬 Έναρξη", subtitle: "Κατά την έναρξη της συνομιλίας εμφανίζεται πάντα το «αρχικό μενού»."),
                new HeroCard(title: "🏁 Ολοκλήρωση", subtitle: "Μόλις ολοκληρωθεί ένας διάλογος, το «αρχικό μενού» εμφανίζεται και πάλι.")
            };

            await stepContext.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(),
                cancellationToken);
        }

        private async Task<DialogTurnResult> MenuContinueStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Intro, stepContext.Options, cancellationToken);
            if (foundChoice.Index == 1)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Actions, stepContext.Options, cancellationToken);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Actions Waterfall Dialog

        private async Task<DialogTurnResult> ActionsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            // TODO: Decide depending on the Role
            bool canEdit = false;
            
            await stepContext.Context.SendActivityAsync("Στη συνέχεια ας δούμε τις διαθέσιμες δυνατότητες:");

            var cards = new HeroCard[5]
            {
                new HeroCard(title: "📚 Εργασίες", subtitle: (canEdit ? "Διαχειρίσου" : "Ανακάλυψε") + " τις εργασίες για επόμενα ή προηγούμενα μαθήματα.")
                {
                    Buttons = new CardAction[1] { new CardAction(type: ActionTypes.ImBack, title: "🦜 Περισσότερα", value: "Περισσότερα για τις εργασίες") }
                },
                new HeroCard(title: "📝 Διαγωνίσματα", subtitle: (canEdit ? "Διαχειρίσου" : "Βρες") + " τα επερχόμενα διαγωνίσματα και τους βαθμούς των παλαιότερων.")
                {
                    Buttons = new CardAction[1] { new CardAction(type: ActionTypes.ImBack, title: "🕊 Περισσότερα", value: "Περισσότερα για τα διαγωνίσματα") }
                },
                new HeroCard(title: "📅 Πρόγραμμα", subtitle:  "Μάθε το πρόγραμμα των μαθημάτων και τυχόν αλλαγές σε αυτό.")
                {
                    Buttons = new CardAction[1] { new CardAction(type: ActionTypes.ImBack, title: "🐧 Περισσότερα", value: "Περισσότερα για το πρόγραμμα") }
                },
                new HeroCard(title: "💪 Βοήθεια", subtitle: "Άνοιξε την τρέχουσα περιήγηση για να λάβεις βοήθεια."),
                new HeroCard(title: "👍 Σχόλια", subtitle: "Βοήθησέ μας να γίνουμε καλύτεροι αφήνοντας ένα σχόλιο.")
            };
            //new HeroCard(title: "💯 Βαθμολογίες", subtitle: "Εισαγωγή των βαθμολογιών των εργασιών και των διαγωνισμάτων."),

            await stepContext.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepContext.PromptAsync(
                PromptNames.HiddenChoices,
                new NavigationPromptOptions()
                {
                    Validations = new string[3] { "Περισσότερα για τις εργασίες", "Περισσότερα για τα διαγωνίσματα", "Περισσότερα για το πρόγραμμα" }
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> ActionsContinueStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Menu, stepContext.Options, cancellationToken);
            if (foundChoice.Index == 1)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Commands, stepContext.Options, cancellationToken);
            if (foundChoice.Index >= 3 && foundChoice.Index <= 5)
            {
                (stepContext.Options as HelpOptions).DetailedAction = foundChoice.Index - 2;
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.ActionDetails, stepContext.Options, cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Action Details Waterfall Dialog

        private async Task<DialogTurnResult> ActionDetailsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var topic = (BotAction)(stepContext.Options as HelpOptions).DetailedAction;
            var topicName = topic == BotAction.Exercise ? "τις ασκήσεις" : topic == BotAction.Exam ? "τα διαγωνίσματα" : "το πρόγραμμα";
            
            await stepContext.Context.SendActivityAsync("Παρακάτω θα βρεις περισσότερες πληροφορίες για " + topicName + ":");

            var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            // TODO: Decide depending on the Role
            bool canEdit = false;

            var cards = topic switch
            {
                BotAction.Exercise => new HeroCard[3]
                {
                    new HeroCard(title: "📚 Eργασίες", subtitle: (canEdit ? "Διαχειρίσου" : "Μάθε") + " τη δουλειά για το σπίτι για κάποιο μάθημα."),
                    new HeroCard(title: "⏭ Για το επόμενο μάθημα", subtitle: (canEdit ? "Διαχειρίσου" : "Δες") + " τις εργασίες για το σπίτι για το επόμενο μάθημα."),
                    new HeroCard(title: "⌚ Για άλλο μάθημα", subtitle: "Κάνε αναζήτηση εργασιών για κάποιου παλαιότερο ή μελλοντικό μάθημα.")
                },
                BotAction.Exam => new HeroCard[4]
                {
                    new HeroCard(title: "📝 Διαγωνίσματα", subtitle: "Προετοιμασία για τα επόμενα διαγωνίσματα και βαθμοί των προηγούμενων."),
                    new HeroCard(title: "💯 Επιδόσεις", subtitle: (canEdit ? "Διαχειρίσου" : "Έλεγξε") + " τη βαθμολογία για παλαιότερα διαγωνίσματα."),
                    new HeroCard(title: "🔮 Μελλοντικά", subtitle: (canEdit ? "Διαχειρίσου" : "Μάθε πότε είναι") + " τα επόμενα διαγωνίσματα."),
                    new HeroCard(title: "🏃 Προετοιμασία", subtitle: (canEdit ? "Διαχειρίσου" : "Δες") + " την ύλη και τι χρειάζεται διάβασμα για ένα προγραμματισμένο διαγώνισμα.")
                },
                BotAction.Schedule => new HeroCard[5]
                {
                    new HeroCard(title: "📅 Πρόγραμμα", subtitle: "Μάθε το πρόγραμμα των μαθημάτων και ενημερώσου για τις αλλαγές του."),
                    new HeroCard(title: "🌞 Σημερινό", subtitle: "Δες τις ώρες και τις αίθουσες για τα σημερινά μαθήματα."),
                    new HeroCard(title: "🌅 Άλλης μέρας", subtitle: "Δες το πρόγραμμα των μαθημάτων για μια μέρα της επιλογής σου."),
                    new HeroCard(title: "🗓 Εβδομαδιαίο", subtitle: "Δες το πρόγραμμα των μαθημάτων σου για την επόμενη εβδομάδα."),
                    new HeroCard(title: "🔄 Αλλαγές/Ενημερώσεις", subtitle: "Οι αλλαγές στο πρόγραμμα εμφανίζονται αυτόματα στις αντίστοιχες ημέρες και ώρες.")
                },
                _ => Array.Empty<HeroCard>()
            };

            await stepContext.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(),
                cancellationToken);
        }

        private async Task<DialogTurnResult> ActionDetailsContinueStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            (stepContext.Options as HelpOptions).DetailedAction = 0;

            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Actions, stepContext.Options, cancellationToken);
            if (foundChoice.Index == 1)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Commands, stepContext.Options, cancellationToken);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Commands Waterfall Dialog

        private async Task<DialogTurnResult> CommandsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Τέλος, ας δούμε τις διάφορες «εντολές», τις οποίες μπορείς να γράψεις ανά πάσα στιγμή:");

            var cards = new HeroCard[6]
            {
                new HeroCard(title: "🔠 Εντολές", subtitle: "Γράφοντας μία εντολή, μπορείς να μεταβείς στην αντίστοιχη συνομιλία."),
                new HeroCard(title: "🎨 Δυνατότητες", subtitle: "Ζήτα μία νέα δραστηριότητα (π.χ. γράψε «πρόγραμμα» ή «εργασίες»)."),
                new HeroCard(title: "🏠 Αρχικό μενού", subtitle: "Γράψε «αρχή» για να εκκινήσεις μία νέα δυνατότητα."),
                new HeroCard(title: "👋 Χαιρετισμοί", subtitle: "Χρησιμοποίησε λέξεις όπως «γεια» ή «καλημέρα» για να σπάσεις τον πάγο."),
                new HeroCard(title: "💪 Βοήθεια", subtitle: "Έχεις κάποια απορία ή έχεις κολλήσει κάπου; Απλά γράψε «βοήθεια»."),
                new HeroCard(title: "👍 Σχόλια", subtitle: "Γράψε «σχόλιο» για να μας πεις την άποψή σου.")
            };

            await stepContext.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(hasNext: false),
                cancellationToken);
        }

        private async Task<DialogTurnResult> CommandsContinueStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Help.Actions, stepContext.Options, cancellationToken);
            if (foundChoice.Index == 1)
                await stepContext.Context.SendActivityAsync("Ελπίζω η περιήγηση να σου φάνηκε χρήσιμη! 😊");

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}

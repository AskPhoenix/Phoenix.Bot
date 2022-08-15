using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs.Prompts.Options;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main.Types;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class HelpDialog : StateDialog
    {
        public HelpDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(HelpDialog))
        {
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

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options,
            CancellationToken canTkn = default)
        {
            var helpOptions = (HelpOptions)options;
            if (helpOptions.AskForTutorial)
                InitialDialogId = WaterfallNames.Help.Ask;

            return base.OnBeginDialogAsync(innerDc, options, canTkn);
        }

        protected override async Task OnEndDialogAsync(ITurnContext context, DialogInstance instance,
            DialogReason reason, CancellationToken canTkn = default)
        {
            await context.SendActivityAsync("Αν χρειαστείς βοήθεια, μη διστάσεις να ακολουθήσεις την περιήγηση αυτή ξανά!");
            
            await base.OnEndDialogAsync(context, instance, reason, canTkn);
        }

        #region Ask Waterfall Dialog

        private async Task<DialogTurnResult> HelpAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να σου δείξω τι μπορώ να κάνω με μια σύντομη περιήγηση;"),
                canTkn);
        }

        private async Task<DialogTurnResult> HelpReplyStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 0)
            {
                await stepCtx.Context.SendActivityAsync(MessageFactory.Text("Τέλεια! 😁 Ας ξεκινήσουμε!"));

                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Intro, stepCtx.Options, canTkn);
            }

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Intro Waterfall Dialog

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Αρχικά, ας δούμε τα βασικά:");

            var cards = new HeroCard[3]
            {
                new HeroCard(title: "🤖 Chatbots", subtitle: "Το chatbot είναι μια εφαρμογή συνομιλίας."),
                new HeroCard(title: "🗣 Συνομιλία", subtitle: "Ένα chatbot περιέχει διαλόγους που υλοποιούν μια συγκεκριμένη δυνατότητα."),
                new HeroCard(title: "💬 Μηνύματα", subtitle: "Η επικοινωνία στους διάφορους διαλόγους γίνεται μέσω μηνυμάτων.")
            };

            await stepCtx.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(hasPrevious: false),
                canTkn);
        }

        private async Task<DialogTurnResult> IntroContinueStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 1)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Menu, stepCtx.Options, canTkn);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Menu Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Τώρα ας δούμε το «αρχικό μενού»:");

            var cards = new HeroCard[3]
            {
                new HeroCard(title: "📜 Αρχικό μενού", subtitle: "Το «αρχικό μενού» περιέχει όλες τις διαθέσιμες δυνατότητες."),
                new HeroCard(title: "🎬 Έναρξη", subtitle: "Κατά την έναρξη της συνομιλίας εμφανίζεται πάντα το «αρχικό μενού»."),
                new HeroCard(title: "🏁 Ολοκλήρωση", subtitle: "Μόλις ολοκληρωθεί ένας διάλογος, το «αρχικό μενού» εμφανίζεται και πάλι.")
            };

            await stepCtx.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(),
                canTkn);
        }

        private async Task<DialogTurnResult> MenuContinueStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 1)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Intro, stepCtx.Options, canTkn);
            if (foundChoice.Index == 2)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Actions, stepCtx.Options, canTkn);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Actions Waterfall Dialog

        private async Task<DialogTurnResult> ActionsStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            bool isStaff = UData.SelectedRole.HasValue && UData.SelectedRole.Value.IsStaff();
            
            await stepCtx.Context.SendActivityAsync("Στη συνέχεια ας δούμε τις διαθέσιμες δυνατότητες:");

            var cards = new HeroCard[5]
            {
                new HeroCard(title: "📚 Εργασίες", subtitle: (isStaff ? "Διαχειρίσου" : "Ανακάλυψε") + " τις εργασίες για επόμενα ή προηγούμενα μαθήματα.")
                {
                    Buttons = new CardAction[1] { new CardAction(type: ActionTypes.ImBack, title: "🦜 Περισσότερα", value: "Περισσότερα για τις εργασίες") }
                },
                new HeroCard(title: "📝 Διαγωνίσματα", subtitle: (isStaff ? "Διαχειρίσου" : "Βρες") + " τα επερχόμενα διαγωνίσματα και τους βαθμούς των παλαιότερων.")
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

            await stepCtx.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepCtx.PromptAsync(
                PromptNames.HiddenChoices,
                new NavigationPromptOptions()
                {
                    Validations = new string[3] { "Περισσότερα για τις εργασίες", "Περισσότερα για τα διαγωνίσματα", "Περισσότερα για το πρόγραμμα" }
                }, canTkn);
        }

        private async Task<DialogTurnResult> ActionsContinueStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 1)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Menu, stepCtx.Options, canTkn);
            if (foundChoice.Index == 2)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Commands, stepCtx.Options, canTkn);
            
            if (foundChoice.Index >= 3 && foundChoice.Index <= 5)
            {
                var options = (HelpOptions)stepCtx.Options;
                options.DetailedAction = foundChoice.Index - 2;

                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.ActionDetails,
                    stepCtx.Options, canTkn);
            }

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Action Details Waterfall Dialog

        private async Task<DialogTurnResult> ActionDetailsStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (HelpOptions)stepCtx.Options;

            var action = options.DetailedAction switch
            {
                1 => BotAction.Exercises,
                2 => BotAction.Exams,
                _ => BotAction.ScheduleWeek
            };

            var actionName = action switch
            {
                BotAction.Exercises     => "τις ασκήσεις",
                BotAction.Exams         => "τα διαγωνίσματα",
                BotAction.ScheduleWeek  => "το πρόγραμμα",
                _                       => string.Empty
            };
            
            await stepCtx.Context.SendActivityAsync(
                "Παρακάτω θα βρεις περισσότερες πληροφορίες για " + actionName + ":");

            bool isStaff = UData.SelectedRole.HasValue && UData.SelectedRole.Value.IsStaff();

            var cards = action switch
            {
                BotAction.Exercises => new HeroCard[3]
                {
                    new HeroCard(title: "📚 Eργασίες", subtitle: (isStaff ? "Διαχειρίσου" : "Μάθε") + " τη δουλειά για το σπίτι για κάποιο μάθημα."),
                    new HeroCard(title: "⏭ Για το επόμενο μάθημα", subtitle: (isStaff ? "Διαχειρίσου" : "Δες") + " τις εργασίες για το σπίτι για το επόμενο μάθημα."),
                    new HeroCard(title: "⌚ Για άλλο μάθημα", subtitle: "Κάνε αναζήτηση εργασιών για κάποιου παλαιότερο ή μελλοντικό μάθημα.")
                },
                BotAction.Exams => new HeroCard[4]
                {
                    new HeroCard(title: "📝 Διαγωνίσματα", subtitle: "Προετοιμασία για τα επόμενα διαγωνίσματα και βαθμοί των προηγούμενων."),
                    new HeroCard(title: "💯 Επιδόσεις", subtitle: (isStaff ? "Διαχειρίσου" : "Έλεγξε") + " τη βαθμολογία για παλαιότερα διαγωνίσματα."),
                    new HeroCard(title: "🔮 Μελλοντικά", subtitle: (isStaff ? "Διαχειρίσου" : "Μάθε πότε είναι") + " τα επόμενα διαγωνίσματα."),
                    new HeroCard(title: "🏃 Προετοιμασία", subtitle: (isStaff ? "Διαχειρίσου" : "Δες") + " την ύλη και τι χρειάζεται διάβασμα για ένα προγραμματισμένο διαγώνισμα.")
                },
                BotAction.ScheduleWeek => new HeroCard[5]
                {
                    new HeroCard(title: "📅 Πρόγραμμα", subtitle: "Μάθε το πρόγραμμα των μαθημάτων και ενημερώσου για τις αλλαγές του."),
                    new HeroCard(title: "🌞 Σημερινό", subtitle: "Δες τις ώρες και τις αίθουσες για τα σημερινά μαθήματα."),
                    new HeroCard(title: "🌅 Άλλης μέρας", subtitle: "Δες το πρόγραμμα των μαθημάτων για μια μέρα της επιλογής σου."),
                    new HeroCard(title: "🗓 Εβδομαδιαίο", subtitle: "Δες το πρόγραμμα των μαθημάτων σου για την επόμενη εβδομάδα."),
                    new HeroCard(title: "🔄 Αλλαγές/Ενημερώσεις", subtitle: "Οι αλλαγές στο πρόγραμμα εμφανίζονται αυτόματα στις αντίστοιχες ημέρες και ώρες.")
                },
                _ => Array.Empty<HeroCard>()
            };

            await stepCtx.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(),
                canTkn);
        }

        private async Task<DialogTurnResult> ActionDetailsContinueStepAsync(WaterfallStepContext stepCtx, CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;
            var options = (HelpOptions)stepCtx.Options;
            
            options.DetailedAction = 0;
            
            if (foundChoice.Index == 1)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Actions, stepCtx.Options, canTkn);
            if (foundChoice.Index == 2)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Commands, stepCtx.Options, canTkn);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Commands Waterfall Dialog

        private async Task<DialogTurnResult> CommandsStepAsync(WaterfallStepContext stepCtx, CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Τέλος, ας δούμε τις διάφορες «εντολές», τις οποίες μπορείς να γράψεις ανά πάσα στιγμή:");

            var cards = new HeroCard[6]
            {
                new HeroCard(title: "🔠 Εντολές", subtitle: "Γράφοντας μία εντολή, μπορείς να μεταβείς στην αντίστοιχη συνομιλία."),
                new HeroCard(title: "🎨 Δυνατότητες", subtitle: "Ζήτα μία νέα δραστηριότητα (π.χ. γράψε «πρόγραμμα» ή «εργασίες»)."),
                new HeroCard(title: "🏠 Αρχικό μενού", subtitle: "Γράψε «αρχή» για να εκκινήσεις μία νέα δυνατότητα."),
                new HeroCard(title: "👋 Χαιρετισμοί", subtitle: "Χρησιμοποίησε λέξεις όπως «γεια» ή «καλημέρα» για να σπάσεις τον πάγο."),
                new HeroCard(title: "💪 Βοήθεια", subtitle: "Έχεις κάποια απορία ή έχεις κολλήσει κάπου; Απλά γράψε «βοήθεια»."),
                new HeroCard(title: "👍 Σχόλια", subtitle: "Γράψε «σχόλιο» για να μας πεις την άποψή σου.")
            };

            await stepCtx.Context.SendActivityAsync(MessageFactory.Carousel(cards.Select(c => c.ToAttachment())));

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new NavigationPromptOptions(hasNext: false),
                canTkn);
        }

        private async Task<DialogTurnResult> CommandsContinueStepAsync(WaterfallStepContext stepCtx, CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 1)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Help.Actions, stepCtx.Options, canTkn);
                
            await stepCtx.Context.SendActivityAsync("Ελπίζω η περιήγηση να σου φάνηκε χρήσιμη! 😊");

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion
    }
}

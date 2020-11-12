using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Schema;
using System.Collections.Generic;
using Phoenix.DataHandle.Main;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Channels.Facebook.FacebookEvents;
using Phoenix.Bot.Utilities.Dialogs.Prompts;

namespace Phoenix.Bot.Dialogs
{
    public class WelcomeDialog : ComponentDialog
    {
        private readonly BotState _userState;

        private static class WaterfallNames
        {
            public const string AskForTutorial  = "AskForTutorial_WaterfallDialog";
            public const string Tutorial        = "Tutorial_WaterfallDialog";
        }

        public WelcomeDialog(UserState userState)
            : base(nameof(WelcomeDialog))
        {
            _userState = userState;

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.AskForTutorial,
                new WaterfallStep[]
                {
                    AskStepAsync,
                    ReplyStepAsync,
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Tutorial,
                new WaterfallStep[]
                {
                    TutorialTopicsStepAsync,
                    TopicRedirectStepAsync,
                    AfterTopicStepAsync,
                    FinalStepAsync
                }));
        }

        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            string mess = innerDc.Context.Activity.Text;
            Persistent.TryGetCommand(mess, out Persistent.Command cmd);

            InitialDialogId = WaterfallNames.AskForTutorial;

            if (cmd == Persistent.Command.Tutorial)
            {
                await innerDc.Context.SendActivityAsync(MessageFactory.Text("Ας κάνουμε μια σύντομη περιήγηση!"));
                InitialDialogId = WaterfallNames.Tutorial;
            }
            else if (cmd == Persistent.Command.GetStarted)
                await innerDc.Context.SendActivityAsync(MessageFactory.Text("Καλωσόρισες στο Phoenix! 😁"));

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Ask for Tutorial Waterfall Dialog

        private async Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Προτού ξεκινήσουμε, θα ήθελες να σου δείξω τι μπορώ να κάνω με μια σύντομη περιήγηση;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });

        private async Task<DialogTurnResult> ReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Τέλεια! 😁"));
                return await stepContext.BeginDialogAsync(WaterfallNames.Tutorial, null, cancellationToken);
            }

            var reply = MessageFactory.Text("Έγινε, κανένα πρόβλημα! Ας ξεκινήσουμε λοιπόν!");
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Tutorial Waterfall Dialog

        private async Task<DialogTurnResult> TutorialTopicsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            Role roleSel = await _userState.CreateProperty<Role>("RoleSelected").GetAsync(stepContext.Context);
            bool isStudent = roleSel == Role.Student;

            var topics = new GenericTemplate()
            {
                ImageAspectRatio = "square",
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "☰ Σταθερό μενού",
                        Subtitle = "Ανακάλυψε τις δυνατότητες του σταθερού μενού της συνομιλίας.",
                        ImageUrl = "https://www.bot.askphoenix.gr/assets/persistent_sq2.png",
                        Buttons = new Button[] { new PostbackButton("🦜 Περισσότερα", "Περισσότερα για το σταθερό μενού") }
                    },
                    new GenericElement()
                    {
                        Title = "🏁 Αρχικό μενού",
                        Subtitle = "Μάθε τις δυνατότητες του αρχικού μενού κατά την έναρξη της συνομιλίας.",
                        ImageUrl = $"https://www.bot.askphoenix.gr/assets/home_{(isStudent ? "student" : "teacher")}_emoji_sq.png",
                        Buttons = new Button[] { new PostbackButton("🕊 Περισσότερα", "Περισσότερα για το αρχικό μενού") }
                    },
                    new GenericElement()
                    {
                        Title = "🔠 Εκφράσεις - Εντολές",
                        Subtitle = "Δες τι άλλο μπορείς να γράψεις στο Phoenix.",
                        ImageUrl = "https://www.bot.askphoenix.gr/assets/logo_sq.png",
                        Buttons = new Button[] { new PostbackButton("🐧 Περισσότερα", "Περισσότερα για τις εντολές") }
                    }
                }
            };

            var reply = MessageFactory.Text("Οι δυνατότητες του Phoenix είναι διαθέσιμες από τα δύο μενού: το σταθερό και το αρχικό.");
            await stepContext.Context.SendActivityAsync(reply);

            reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(topics);
            await stepContext.Context.SendActivityAsync(reply);

            reply = (Activity)MessageFactory.SuggestedActions(new CardAction[] { new CardAction(ActionTypes.ImBack, "🔚 Παράλειψη") });
            reply.Text = "Πάτησε στα κουμπιά παραπάνω για να μάθεις περισσότερα, ή επίλεξε \"Παράλειψη\" για να ολοκληρώσεις την περιήγηση.";

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Style = ListStyle.None,
                    Choices = ChoiceFactory.ToChoices(new string[] 
                        { "Περισσότερα για το σταθερό μενού", "Περισσότερα για το αρχικό μενού", "Περισσότερα για τις εντολές", "🔚 Παράλειψη" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> TopicRedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => (stepContext.Result as FoundChoice).Index switch
            {
                0 => await PersistentMenuTutorialStepAsync(stepContext, cancellationToken),
                1 => await HomeTutorialStepAsync(stepContext, cancellationToken),
                2 => await CommandsTutorialStepAsync(stepContext, cancellationToken),
                3 => await FinalStepAsync(stepContext, cancellationToken),
                _ => await stepContext.EndDialogAsync(null, cancellationToken)
            };

        private async Task<DialogTurnResult> PersistentMenuTutorialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text("Για να ανοίξεις το \"Σταθερό μενού\" " +
                "μπορείς να πατήσεις στο κουμπί ☰ που βρίσκεται στο κάτω δεξία μέρος της συνομιλίας μας.");
            await stepContext.Context.SendActivityAsync(reply);

            reply.Text = "Το \"Σταθερό μενού\" είναι διαθέσιμο οποιαδήποτε στιγμή και περιέχει τις παρακάτω επιλογές:";
            await stepContext.Context.SendActivityAsync(reply);

            var persistentCards = new GenericTemplate()
            {
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "🏠 Αρχικό μενού",
                        Subtitle = "Συντόμευση για το \"Αρχικό μενού\" και την επανέναρξη της συνομιλίας."
                    },
                    new GenericElement()
                    {
                        Title = "ℹ️ Τι μπορώ να κάνω!",
                        Subtitle = "Ξεναγήσου ανά πάσα στιγμή στις δυνατότητες του Phoenix με μια σύντομη περιήγηση."
                    },
                    new GenericElement()
                    {
                        Title = "👍 Αφήστε ένα σχόλιο!",
                        Subtitle = "Βοήθησε το Phoenix να γίνει ακόμα καλύτερο κάνοντας ένα σχόλιο ή μια αξιολόγηση."
                    }
                }
            };

            reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(persistentCards);
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> HomeTutorialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text("Το \"Αρχικό μενού\" είναι διαθέσιμο κατά την έναρξη της συνομιλίας μας, " +
                "καθώς και από την αντίστοιχη συντόμευση στο \"Σταθερό μενού\".");
            await stepContext.Context.SendActivityAsync(reply);

            reply.Text = "Οι υπηρεσίες που παρέχει εμφανίζονται παρακάτω εν συντομία:";
            await stepContext.Context.SendActivityAsync(reply);

            Role roleSel = await _userState.CreateProperty<Role>("RoleSelected").GetAsync(stepContext.Context);
            bool isStudent = roleSel == Role.Student;

            var homeCards = new GenericTemplate()
            {
                ImageAspectRatio = "square",
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "📚 Εργασίες",
                        Subtitle = isStudent ? "Έλεγξε τις εργασίες σου για το σπίτι." : "Προσθήκη και επεξεργασία των εργασιών των μαθητών."
                    },
                    new GenericElement()
                    {
                        Title = "📝 Διαγωνίσματα",
                        Subtitle = isStudent ? "Διαχειρίσου τα διαγωνίσματα που έχεις ήδη γράψει ή πρόκειται να γράψεις." 
                            : "Δημιουργία νέων διαγωνισμάτων και επεξεργασία της ύλης."
                    },
                    new GenericElement()
                    {
                        Title = "📅 Πρόγραμμα",
                        Subtitle = isStudent ? "Δες το πρόγραμμα των μαθημάτων σου και τυχόν αλλαγές σε αυτό."
                            : (roleSel > Role.Teacher ? "Εμφάνιση και επεξεργασία των ωρών του προγράμματος διδασκαλίας." 
                            : "Εμφάνιση των ωρών του προγράμματος διδασκαλίας.")
                    }
                }
            };

            if (!isStudent)
            {
                var elements = new List<GenericElement>(homeCards.Elements);
                elements.Insert(2, new GenericElement() 
                {
                    Title = "💯 Βαθμολογίες",
                    Subtitle = "Εισαγωγή των βαθμολογιών των εργασιών και των διαγωνισμάτων." 
                });
                homeCards.Elements = elements.ToArray();
            }

            reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(homeCards);
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> CommandsTutorialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text("Εκτός από τα μενού, υπάρχουν και οι παρακάτω εκφράσεις, " +
                "τις οποίες μπορείς να πληκτρολογήσεις ανά πάσα στιγμή:");
            await stepContext.Context.SendActivityAsync(reply);

            var commandsCards = new GenericTemplate()
            {
                Elements = new GenericElement[]
                {
                    new GenericElement()
                    {
                        Title = "👋 Χαιρετισμοί",
                        Subtitle = "Χαιρέτισε γράφοντας \"Γεια σου Phoenix!\", \"Καλημέρα\" ή παρόμοιες εκφράσεις."
                    },
                    new GenericElement()
                    {
                        Title = "💪 Βοήθεια",
                        Subtitle = "Έχεις κάποια αποροία ή έχεις κολλήσει κάπου; Απλά γράψε \"Βοήθεια\"."
                    }
                }
            };

            reply = stepContext.Context.Activity.CreateReply();
            reply.ChannelData = ChannelDataFactory.Template(commandsCards);
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> AfterTopicStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να συνεχίσεις την περιήγηση εξερευνώντας κάποιο άλλο θέμα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω για να συνεχίσουμε:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "🔙 Άλλο θέμα", "🔚 Ολοκλήρωση" }),
                    Style = ListStyle.SuggestedAction
                },
                cancellationToken);

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice != null && foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, stepContext.Options, cancellationToken);

            var reply = MessageFactory.Text("Ελπίζω η περιήγηση να σου φάνηκε χρήσιμη! 😊");
            await stepContext.Context.SendActivityAsync(reply);

            reply.Text = "Εάν τη χρειαστείς ξανά, θα είναι διαθέσιμη μέσω του \"Σταθερού μενού\".";
            await stepContext.Context.SendActivityAsync(reply);

            reply.Text = "Τέλος, αν έχεις απορίες σχετικά με κάποια δυνατότητα, μπορείς να πληκτρολογήσεις \"Βοήθεια\".";
            await stepContext.Context.SendActivityAsync(reply);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}

using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class GradesDialog : ComponentDialog
    {
        private readonly ExamRepository examRepository;

        public GradesDialog(PhoenixContext phoenixContext)
            : base(nameof(GradesDialog))
        {
            examRepository = new ExamRepository(phoenixContext);

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Top,
                new WaterfallStep[]
                {
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Recent,
                new WaterfallStep[]
                {

                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Search,
                new WaterfallStep[]
                {

                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Mark,
                new WaterfallStep[]
                {

                }));

            InitialDialogId = WaterfallNames.Actions.Grades.Top;
        }
    }
}

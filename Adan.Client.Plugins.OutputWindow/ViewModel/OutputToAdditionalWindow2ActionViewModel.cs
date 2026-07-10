namespace Adan.Client.Plugins.OutputWindow
{
    using System.Collections.Generic;
    using Common.Plugins;
    using Common.Themes;
    using Common.ViewModel;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Model.Actions;

    public class OutputToAdditionalWindow2ActionViewModel : ActionWithParametersViewModelBase
    {
        private readonly OutputToAdditionalWindow2Action _action;

        public OutputToAdditionalWindow2ActionViewModel([NotNull] OutputToAdditionalWindow2Action action, [NotNull] ActionDescription actionDescriptor, [NotNull] IEnumerable<ParameterDescription> parameterDescriptions, [NotNull] IEnumerable<ActionDescription> allDescriptions)
            : base(action, actionDescriptor, parameterDescriptions, allDescriptions)
        {
            Assert.ArgumentNotNull(action, "action");
            Assert.ArgumentNotNull(actionDescriptor, "actionDescriptor");
            Assert.ArgumentNotNull(parameterDescriptions, "parameterDescriptions");
            Assert.ArgumentNotNull(allDescriptions, "allDescriptions");
            _action = action;
        }

        [NotNull]
        public string TextToOutput
        {
            get { return _action.TextToOutput; }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                _action.TextToOutput = value;
                OnPropertyChanged("TextToOutput");
                OnPropertyChanged("ActionDescription");
            }
        }

        public TextColor TextColor
        {
            get { return _action.TextColor; }
            set { _action.TextColor = value; OnPropertyChanged("TextColor"); }
        }

        public TextColor BackgroundColor
        {
            get { return _action.BackgroundColor; }
            set { _action.BackgroundColor = value; OnPropertyChanged("BackgroundColor"); }
        }

        public bool OutputEntireMessageKeepingColors
        {
            get { return _action.OutputEntireMessageKeepingColors; }
            set
            {
                _action.OutputEntireMessageKeepingColors = value;
                OnPropertyChanged("OutputEntireMessageKeepingColors");
                OnPropertyChanged("ActionDescription");
            }
        }

        public override string ActionDescription
        {
            get
            {
                if (OutputEntireMessageKeepingColors)
                    return "#output2";
                return "#output2 " + TextToOutput + ParametersModel.ActionParametersDescription;
            }
        }

        public override ActionViewModelBase Clone()
        {
            var action = new OutputToAdditionalWindow2Action();
            return new OutputToAdditionalWindow2ActionViewModel(action, ActionDescriptor, ParametersModel.ParameterDescriptions, AllActionDescriptions)
            {
                BackgroundColor = BackgroundColor,
                TextColor = TextColor,
                TextToOutput = TextToOutput,
                OutputEntireMessageKeepingColors = OutputEntireMessageKeepingColors,
                ParametersModel = ParametersModel.Clone(action.Parameters)
            };
        }
    }
}

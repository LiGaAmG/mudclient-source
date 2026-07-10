namespace Adan.Client.Plugins.OutputWindow.Model
{
    using System.Collections.Generic;
    using Common.Model;
    using Common.Plugins;
    using Common.ViewModel;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Actions;

    public class OutputToAdditionalWindow2ActionDescription : ActionDescription
    {
        private readonly IEnumerable<ParameterDescription> _parameterDescriptions;

        public OutputToAdditionalWindow2ActionDescription([NotNull] IEnumerable<ParameterDescription> parameterDescriptions, [NotNull] IEnumerable<ActionDescription> allDescriptions)
            : base("Output to additional window 2", allDescriptions)
        {
            Assert.ArgumentNotNull(parameterDescriptions, "parameterDescriptions");
            Assert.ArgumentNotNull(allDescriptions, "allDescriptions");
            _parameterDescriptions = parameterDescriptions;
        }

        public override ActionBase CreateAction()
        {
            return new OutputToAdditionalWindow2Action();
        }

        public override ActionViewModelBase CreateActionViewModel(ActionBase action)
        {
            Assert.ArgumentNotNull(action, "action");
            var outputAction = action as OutputToAdditionalWindow2Action;
            if (outputAction != null)
            {
                return new OutputToAdditionalWindow2ActionViewModel(outputAction, this, _parameterDescriptions, AllDescriptions);
            }
            return null;
        }
    }
}

// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AffectDescription.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the AffectDescription type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Plugins.GroupWidget.ViewModel
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Describse affect.
    /// </summary>
    public class AffectDescription : ViewModelBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AffectDescription"/> class.
        /// </summary>
        /// <param name="name">The name to store in settings.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="affectName">Name of the affect.</param>
        /// <param name="icon">The icon to display.</param>
        public AffectDescription([NotNull] string name, [NotNull] string displayName, [NotNull] string affectName, [NotNull] string icon)
        {
            Assert.ArgumentNotNullOrWhiteSpace(name, "name");
            Assert.ArgumentNotNullOrWhiteSpace(displayName, "displayName");
            Assert.ArgumentNotNullOrWhiteSpace(affectName, "affectName");
            Assert.ArgumentNotNullOrWhiteSpace(icon, "icon");

            AffectNames = new List<string> { affectName };
            Icons = new List<string> { icon };
            Name = name;
            DisplayName = displayName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AffectDescription"/> class.
        /// </summary>
        /// <param name="name">The name to store in settings.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="affectNames">The affect names.</param>
        /// <param name="icons">The icons.</param>
        public AffectDescription([NotNull] string name, [NotNull] string displayName, [NotNull] IEnumerable<string> affectNames, [NotNull] IEnumerable<string> icons)
        {
            Assert.ArgumentNotNullOrWhiteSpace(name, "name");
            Assert.ArgumentNotNullOrWhiteSpace(displayName, "displayName");
            Assert.ArgumentNotNull(affectNames, "affectNames");
            Assert.ArgumentNotNull(icons, "icons");

            AffectNames = new List<string>(affectNames);
            Icons = new List<string>(icons);
            Name = name;
            DisplayName = displayName;
        }

        /// <summary>
        /// Gets the name to store in settings.
        /// </summary>
        [NotNull]
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        [NotNull]
        public string DisplayName
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the affect names.
        /// </summary>
        [NotNull]
        public IList<string> AffectNames
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the icons.
        /// </summary>
        public IList<string> Icons
        {
            get;
            private set;
        }

        public bool Matches([NotNull] string affectName)
        {
            Assert.ArgumentNotNull(affectName, "affectName");

            return AffectNames.Any(name => AreAffectNamesEqual(name, affectName));
        }

        [NotNull]
        public string GetIcon([NotNull] string affectName)
        {
            Assert.ArgumentNotNull(affectName, "affectName");

            for (var i = 0; i < AffectNames.Count; i++)
            {
                if (AreAffectNamesEqual(AffectNames[i], affectName))
                {
                    return Icons[i];
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the default icon.
        /// </summary>
        [NotNull]
        public string DefaultIcon
        {
            get
            {
                return Icons.First();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this affect is round based.
        /// </summary>
        /// <value>
        /// <c>true</c> if duration of this affect will be round based; otherwise <c>false</c>.
        /// </value>
        public bool IsRoundBased
        {
            get;
            set;
        }

        private static bool AreAffectNamesEqual([NotNull] string left, [NotNull] string right)
        {
            return string.Equals(NormalizeAffectName(left), NormalizeAffectName(right), StringComparison.OrdinalIgnoreCase);
        }

        [NotNull]
        private static string NormalizeAffectName([NotNull] string value)
        {
            return value.Trim().Replace('_', ' ');
        }
    }
}

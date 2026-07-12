// --------------------------------------------------------------------------------------------------------------------
// <copyright file="GroupWidgetControl.xaml.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Interaction logic for GroupWidgetControl.xaml
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Plugins.GroupWidget
{
    using System;
    using System.Linq;
    using System.Windows.Controls;
    using System.Windows.Input;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using ViewModel;
    using Common.Model;
    using System.Collections.Generic;
    using System.Windows.Threading;
    using System.Windows;

    /// <summary>
    /// Interaction logic for GroupWidgetControl.xaml
    /// </summary>
    public partial class GroupWidgetControl : UserControl
    {
        private readonly object _stack_lock = new object();
        private readonly Stack<List<CharacterStatus>> _charactrers_stack = new Stack<List<CharacterStatus>>();
        private bool _updatePending = false;
        private long _updateQueuedTick;
        // Дроссель: не чаще 1 раза в 150мс. Таймер стреляет один раз, диспатчит на UI-поток.
        private readonly System.Threading.Timer _updateThrottle;


        /// <summary>
        /// Initializes a new instance of the <see cref="GroupWidgetControl"/> class.
        /// </summary>
        public GroupWidgetControl()
        {
            InitializeComponent();
            IconRasterizer.RasterizeIcons(Resources);
            _updateThrottle = new System.Threading.Timer(OnUpdateThrottleTimer, null,
                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        private void OnUpdateThrottleTimer(object state)
        {
            List<CharacterStatus> list;
            long queuedTick;
            lock (_stack_lock)
            {
                _updatePending = false;
                if (_charactrers_stack.Count == 0) return;
                list = _charactrers_stack.Pop();
                _charactrers_stack.Clear();
                queuedTick = _updateQueuedTick;
            }

            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    long executeTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    long waitedMs = (long)((executeTick - queuedTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    var updateSw = System.Diagnostics.Stopwatch.StartNew();
                    var viewModel = DataContext as GroupStatusViewModel;
                    if (viewModel != null)
                        viewModel.UpdateModel(list);
                    updateSw.Stop();
                    if (waitedMs >= 50 || updateSw.ElapsedMilliseconds >= 5)
                        Common.Conveyor.PerfLog.WriteWidget("GroupWidget", waitedMs, updateSw.ElapsedMilliseconds);
                }
                catch (Exception) { }
            }));
        }

        /// <summary>
        /// 
        /// </summary>
        public string ViewModelUid
        {
            get;
            set;
        }

        /// <summary>
        /// 
        /// </summary>
        public void NextGroupMate()
        {
            Action executeToAct = () =>
            {
                if (DataContext != null)
                {
                    var vm = (GroupStatusViewModel)DataContext;
                    var active = vm.GroupMates.Where(m => m.IsActive).ToList();
                    if (vm.SelectedGroupMate == null || active.IndexOf(vm.SelectedGroupMate) == active.Count - 1)
                    {
                        vm.SelectedGroupMate = active.FirstOrDefault();
                        return;
                    }

                    var index = active.IndexOf(vm.SelectedGroupMate);
                    vm.SelectedGroupMate = active[index + 1];
                }
            };

            Application.Current.Dispatcher.BeginInvoke(executeToAct, DispatcherPriority.Background);
        }

        /// <summary>
        ///
        /// </summary>
        public void PreviousGroupMate()
        {
            Action executeToAct = () =>
            {
                if (DataContext != null)
                {
                    var vm = (GroupStatusViewModel)DataContext;
                    var active = vm.GroupMates.Where(m => m.IsActive).ToList();
                    if (vm.SelectedGroupMate == null || active.IndexOf(vm.SelectedGroupMate) == 0)
                    {
                        vm.SelectedGroupMate = active.LastOrDefault();
                        return;
                    }

                    var index = active.IndexOf(vm.SelectedGroupMate);
                    vm.SelectedGroupMate = active[index - 1];
                }
            };

            Application.Current.Dispatcher.BeginInvoke(executeToAct, DispatcherPriority.Background);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="characters"></param>
        public void UpdateModel([NotNull] List<CharacterStatus> characters)
        {
            Assert.ArgumentNotNull(characters, "characters");

            lock (_stack_lock)
            {
                _charactrers_stack.Push(characters);
                if (!_updatePending)
                {
                    _updatePending = true;
                    _updateQueuedTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    _updateThrottle.Change(150, System.Threading.Timeout.Infinite);
                }
            }
        }

        private void CancelFocusingListBoxItem([NotNull] object sender, [NotNull] MouseButtonEventArgs e)
        {
            Assert.ArgumentNotNull(sender, "sender");
            Assert.ArgumentNotNull(e, "e");

            ((ListBoxItem)sender).IsSelected = true;
            e.Handled = true;
        }
    }
}


// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MonstersWidgetControl.xaml.cs" company="Adamand MUD">
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
    using System.Windows;
    using System.Windows.Threading;

    /// <summary>
    /// Interaction logic for GroupWidgetControl.xaml
    /// </summary>
    public partial class MonstersWidgetControl : UserControl
    {
        private readonly object _stack_lock = new object();
        private readonly Stack<List<MonsterStatus>> _monsters_stack = new Stack<List<MonsterStatus>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MonstersWidgetControl"/> class.
        /// </summary>
        public MonstersWidgetControl()
        {
            InitializeComponent();
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
        public void NextMonster()
        {
            Action executeToAct = () =>
            {
                if (this.DataContext != null)
                {
                    RoomMonstersViewModel _roomMonstersViewModel = (RoomMonstersViewModel)this.DataContext;
                    if (_roomMonstersViewModel.SelectedMonster == null || _roomMonstersViewModel.Monsters.IndexOf(_roomMonstersViewModel.SelectedMonster) == _roomMonstersViewModel.Monsters.Count - 1)
                    {
                        _roomMonstersViewModel.SelectedMonster = _roomMonstersViewModel.Monsters.FirstOrDefault();
                        return;
                    }

                    var index = _roomMonstersViewModel.Monsters.IndexOf(_roomMonstersViewModel.SelectedMonster);
                    _roomMonstersViewModel.SelectedMonster = _roomMonstersViewModel.Monsters[index + 1];
                }
            };

            Application.Current.Dispatcher.BeginInvoke(executeToAct, DispatcherPriority.Background);
        }

        /// <summary>
        /// 
        /// </summary>
        public void PreviousMonster()
        {
            Action executeToAct = () =>
            {
                if (this.DataContext != null)
                {
                    RoomMonstersViewModel _roomMonstersViewModel = (RoomMonstersViewModel)this.DataContext;
                    if (_roomMonstersViewModel.SelectedMonster == null || _roomMonstersViewModel.Monsters.IndexOf(_roomMonstersViewModel.SelectedMonster) == 0)
                    {
                        _roomMonstersViewModel.SelectedMonster = _roomMonstersViewModel.Monsters.LastOrDefault();
                        return;
                    }

                    var index = _roomMonstersViewModel.Monsters.IndexOf(_roomMonstersViewModel.SelectedMonster);
                    _roomMonstersViewModel.SelectedMonster = _roomMonstersViewModel.Monsters[index - 1];
                }
            };

            Application.Current.Dispatcher.BeginInvoke(executeToAct, DispatcherPriority.Background);
        }

        private bool _pendingIsRound = true;
        private bool _updatePending = false;

        public void UpdateModel([NotNull] List<MonsterStatus> characters, bool isRound = true)
        {
            Assert.ArgumentNotNull(characters, "roomMonstersMessage");

#if DEBUG
            long queuedTick = System.Diagnostics.Stopwatch.GetTimestamp();
#endif

            Action actToExecute = () =>
            {
                try
                {
#if DEBUG
                    long executeTick = System.Diagnostics.Stopwatch.GetTimestamp();
                    long waitedMs = (long)((executeTick - queuedTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    var updateSw = System.Diagnostics.Stopwatch.StartNew();
#endif
                    RoomMonstersViewModel viewModel = DataContext as RoomMonstersViewModel;

                    List<MonsterStatus> list = null;
                    bool pendingIsRound;
                    lock (_stack_lock)
                    {
                        _updatePending = false;
                        if (_monsters_stack.Count > 0)
                        {
                            list = _monsters_stack.Pop();
                            _monsters_stack.Clear();
                        }
                        pendingIsRound = _pendingIsRound;
                    }

                    if (list != null)
                    {
                        viewModel.UpdateModel(list, pendingIsRound);
                    }
#if DEBUG
                    updateSw.Stop();
                    if (waitedMs >= 20 || updateSw.ElapsedMilliseconds >= 5)
                        Common.Conveyor.PerfLog.WriteWidget("MonstersWidget", waitedMs, updateSw.ElapsedMilliseconds);
#endif
                }
                catch (Exception) { }
            };

            bool needInvoke;
            lock (_stack_lock)
            {
                _monsters_stack.Push(characters);
                _pendingIsRound = isRound;
                needInvoke = !_updatePending;
                if (needInvoke) _updatePending = true;
            }

            if (needInvoke)
                Application.Current.Dispatcher.BeginInvoke(actToExecute, DispatcherPriority.Background);
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

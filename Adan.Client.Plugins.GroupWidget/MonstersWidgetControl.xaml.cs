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
        private List<MonsterStatus> _lastRawMonsters;

        public List<MonsterStatus> GetLastMonsters()
        {
            lock (_stack_lock) { return _lastRawMonsters; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MonstersWidgetControl"/> class.
        /// </summary>
        public MonstersWidgetControl()
        {
            InitializeComponent();
            IconRasterizer.RasterizeIcons(Resources);

            // Скрытый виджет не обновляем (зря пересоздавали VM и дёргали layout).
            // Отложенный список лежит в стеке; при показе применяем самый свежий.
            IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                    ProcessPendingUpdate();
            };
        }

        private void ProcessPendingUpdate()
        {
            try
            {
                RoomMonstersViewModel viewModel = DataContext as RoomMonstersViewModel;
                if (viewModel == null) return;

                List<MonsterStatus> list = null;
                lock (_stack_lock)
                {
                    if (_monsters_stack.Count > 0)
                    {
                        list = _monsters_stack.Pop();
                        _monsters_stack.Clear();
                    }
                }

                if (list != null)
                {
                    viewModel.UpdateModel(list);
                }
            }
            catch (Exception) { }
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
                    // Спящие строки пула в переборе не участвуют
                    var active = _roomMonstersViewModel.Monsters.Where(m => m.IsActive).ToList();
                    if (_roomMonstersViewModel.SelectedMonster == null || active.IndexOf(_roomMonstersViewModel.SelectedMonster) == active.Count - 1)
                    {
                        _roomMonstersViewModel.SelectedMonster = active.FirstOrDefault();
                        return;
                    }

                    var index = active.IndexOf(_roomMonstersViewModel.SelectedMonster);
                    _roomMonstersViewModel.SelectedMonster = active[index + 1];
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
                    var active = _roomMonstersViewModel.Monsters.Where(m => m.IsActive).ToList();
                    if (_roomMonstersViewModel.SelectedMonster == null || active.IndexOf(_roomMonstersViewModel.SelectedMonster) <= 0)
                    {
                        _roomMonstersViewModel.SelectedMonster = active.LastOrDefault();
                        return;
                    }

                    var index = active.IndexOf(_roomMonstersViewModel.SelectedMonster);
                    _roomMonstersViewModel.SelectedMonster = active[index - 1];
                }
            };

            Application.Current.Dispatcher.BeginInvoke(executeToAct, DispatcherPriority.Background);
        }

        public void UpdateModel([NotNull] List<MonsterStatus> characters)
        {
            Assert.ArgumentNotNull(characters, "characters");

            Action actToExecute = () =>
            {
                // Виджет скрыт — список остаётся в стеке и применится при показе
                // (IsVisibleChanged) или со следующим обновлением, когда виджет видим.
                if (!IsVisible) return;
                ProcessPendingUpdate();
            };

            lock (_stack_lock)
            {
                _lastRawMonsters = characters;
                _monsters_stack.Push(characters);
            }

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

namespace Adan.Client.Plugins.GroupWidget.ViewModel
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;

    /// <summary>
    /// ObservableCollection с поддержкой пакетных обновлений.
    /// AddRange/ReplaceAll подавляют промежуточные уведомления и посылают один Reset в конце,
    /// что позволяет WPF сделать один layout-проход вместо N.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications;

        /// <summary>
        /// Добавляет все элементы и посылает один Reset-notification.
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            _suppressNotifications = true;
            try
            {
                foreach (var item in items)
                    Items.Add(item);
            }
            finally
            {
                _suppressNotifications = false;
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Заменяет содержимое коллекции новым списком за один Reset-notification.
        /// Эффективнее чем Clear() + N×Add().
        /// </summary>
        public void ReplaceAll(IList<T> newItems)
        {
            _suppressNotifications = true;
            try
            {
                Items.Clear();
                foreach (var item in newItems)
                    Items.Add(item);
            }
            finally
            {
                _suppressNotifications = false;
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>
        /// Обновляет коллекцию без Reset-notification.
        /// Для одинакового числа элементов: индивидуальные Replace-нотификации — WPF
        /// переиспользует тот же контейнер/DataTemplate, меняя только DataContext.
        /// Для изменения числа элементов: Replace первых min(old,new) + Add/Remove дельты.
        /// Итог: нет глобального destroy/rebuild цикла для 30 контейнеров.
        /// </summary>
        public void UpdateItems(IList<T> newItems)
        {
            int oldCount = Count;
            int newCount = newItems.Count;
            int minCount = oldCount < newCount ? oldCount : newCount;

            // Update in-place for positions that exist in both old and new list
            for (int i = 0; i < minCount; i++)
            {
                if (!ReferenceEquals(Items[i], newItems[i]))
                    this[i] = newItems[i]; // fires individual Replace notification
            }

            if (newCount > oldCount)
            {
                // New list is longer — append extra items
                for (int i = oldCount; i < newCount; i++)
                    Add(newItems[i]); // fires individual Add notification
            }
            else if (newCount < oldCount)
            {
                // New list is shorter — remove from the end
                while (Count > newCount)
                    RemoveAt(Count - 1); // fires individual Remove notification
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications)
                base.OnCollectionChanged(e);
        }
    }
}

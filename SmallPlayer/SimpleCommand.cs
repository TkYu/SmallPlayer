using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmallPlayer
{
    public class SimpleCommand : ICommand
    {
        public Predicate<object> CanExecuteDelegate { get; set; }
        public Action ExecuteDelegate { get; set; }
        public SimpleCommand(Action execute, Predicate<object> canExecute = null)
        {
            CanExecuteDelegate = canExecute;
            ExecuteDelegate = execute;
        }
        public bool CanExecute(object parameter)
        {
            return CanExecuteDelegate == null || CanExecuteDelegate(parameter);
        }
        public void Execute(object parameter)
        {
            ExecuteDelegate?.Invoke();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
    public class SimpleCommand<T> : ICommand
    {
        public Predicate<object> CanExecuteDelegate { get; set; }
        public Action<T> ExecuteDelegate { get; set; }

        public SimpleCommand(Action<T> execute, Predicate<object> canExecute = null)
        {
            CanExecuteDelegate = canExecute;
            ExecuteDelegate = execute;
        }
        public bool CanExecute(object parameter)
        {
            return CanExecuteDelegate == null || CanExecuteDelegate(parameter);
        }
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void Execute(object parameter)
        {
            ExecuteDelegate?.Invoke((T)parameter);
        }
    }
}

﻿using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HashCalculator
{
    public abstract class NotifiableModel : INotifyPropertyChanged
    {
        public void NotifyPropertyChanged([CallerMemberName] string name = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void SetPropNotify<T>(ref T property, T value, [CallerMemberName] string name = null)
        {
            property = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

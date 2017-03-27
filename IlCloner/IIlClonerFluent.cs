using System;
using System.Linq.Expressions;

namespace IlCloningGenerator
{
    public interface IIlClonerFluent<T>
    {
        IlCloner Cloner { get; }

        IIlClonerFluent<TOther> Configure<TOther>();
        Func<T, T> CreateCloner();
        IIlClonerFluent<T> Exclude<TMember>(Expression<Func<T, TMember>> selector);
        IIlClonerFluent<T> Include<TMember>(Expression<Func<T, TMember>> selector);
        IIlClonerFluent<T> AlwaysStraightCopy<TMember>(Expression<Func<T, TMember>> selector);
    }
}
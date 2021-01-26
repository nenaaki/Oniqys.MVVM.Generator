using System;
using System.Xml;

namespace Sample
{
    class Program
    {
        static void Main(string[] args)
        {

            var viewModel = new FooViewModel();

            viewModel.PropertyChanged += (s, e) => Console.WriteLine($"{e.PropertyName} : {viewModel.Value}");

            viewModel.Value++;
        }
    }
}

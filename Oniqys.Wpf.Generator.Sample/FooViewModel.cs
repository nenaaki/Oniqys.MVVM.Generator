using Oniqys.Wpf.Generator;

namespace Sample
{
    public partial class FooViewModel
    {
        [NotifiableProperty(PropertyName = "Value")]
        private int _value;
    }
}

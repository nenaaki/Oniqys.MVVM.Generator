using Oniqys.Wpf.Generator;

namespace Sample
{
    public partial class FooViewModel
    {
        /// <summary>
        /// 値です。
        /// </summary>
        [NotifiableProperty(PropertyName = "Value")]
        private int _value;
    }
}

using System;

namespace Oniqys.Wpf.Generator
{
    /// <summary>
    /// 通知型のプロパティをフィールドに付与します。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class NotifiablePropertyAttribute : Attribute
    {
        public NotifiablePropertyAttribute() { }

        /// <summary>
        /// 自動生成後のプロパティ名です。
        /// </summary>
        public string PropertyName { get; set; }
    }
}

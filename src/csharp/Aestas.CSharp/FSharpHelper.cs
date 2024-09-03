namespace Aestas.CSharp
{
    public static class FSharpHelper
    {
#pragma warning disable CS8601 // 引用类型赋值可能为 null。
        public readonly static Unit UnitValue = System.Runtime.CompilerServices.Unsafe.As<Unit>(null);
#pragma warning restore CS8601 // 引用类型赋值可能为 null。
        public static FSharpOption<T> MakeSome<T>(T t) => FSharpOption<T>.Some(t);
        public static FSharpOption<T> NoneValue<T>() => FSharpOption<T>.None;
    }
}

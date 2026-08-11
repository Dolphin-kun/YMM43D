using System.Runtime.CompilerServices;

namespace YMM43D.Plugin
{
    /// <summary>
    /// YMM4 のパラメータオブジェクトと、それが生み出した <see cref="I3DProvider"/> の対応表。
    /// </summary>
    /// <remarks>
    /// 3Dプレビューはタイムライン上のアイテムしか辿れませんが、実際に描画できるのは
    /// アイテムが生成したソースやプロセッサの側です。両者を結びつけるためにこの表を使います。
    /// <para>
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> なので、パラメータが破棄されれば
    /// 対応するプロバイダーの参照も自動的に外れます。
    /// </para>
    /// </remarks>
    public static class Provider3DRegistry
    {
        private static readonly ConditionalWeakTable<object, I3DProvider> registry = [];

        /// <summary>
        /// パラメータに対応するプロバイダーを登録します。既存の登録は上書きされます。
        /// </summary>
        public static void Register(object parameter, I3DProvider provider)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(provider);

            registry.Remove(parameter);
            registry.Add(parameter, provider);
        }

        /// <summary>登録を解除します。</summary>
        public static void Unregister(object? parameter)
        {
            if (parameter is not null)
                registry.Remove(parameter);
        }

        /// <summary>
        /// パラメータに対応するプロバイダーを取得します。未登録なら <c>null</c>。
        /// </summary>
        public static I3DProvider? Find(object? parameter)
        {
            if (parameter is null)
                return null;

            registry.TryGetValue(parameter, out var provider);
            return provider;
        }
    }
}

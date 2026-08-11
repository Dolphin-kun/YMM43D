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

        [ThreadStatic]
        private static int suppressionDepth;

        /// <summary>
        /// この場を抜けるまで、<see cref="Register"/> の呼び出しを無視します。
        /// </summary>
        /// <remarks>
        /// 3Dプレビューは、アイテムの変換行列や画像を得るために YMM4 の描画元を
        /// もう一組作ることがあります。その過程で生まれるプロバイダーは一時的なもので、
        /// 本来のプロバイダーを置き換えてしまってはいけません。
        /// <para>
        /// 置き換わると、登録が差し替わる一瞬だけ <see cref="Find"/> が <c>null</c> を
        /// 返し、アイテムが既定の描画方法（板にテクスチャを貼る）で表示されてしまいます。
        /// </para>
        /// </remarks>
        public static IDisposable SuppressRegistration() => new Suppression();

        /// <summary>
        /// パラメータに対応するプロバイダーを登録します。既存の登録は上書きされます。
        /// </summary>
        public static void Register(object parameter, I3DProvider provider)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(provider);

            if (suppressionDepth > 0)
                return;

            registry.Remove(parameter);
            registry.Add(parameter, provider);
        }

        private sealed class Suppression : IDisposable
        {
            private bool disposed;

            public Suppression() => suppressionDepth++;

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                suppressionDepth--;
            }
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

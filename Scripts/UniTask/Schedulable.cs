﻿using System;
using System.Collections;
using System.Collections.Generic;
#if (NET_4_6 && UNITY_2018_1_OR_NEWER) 
using System.Threading.Tasks;
#endif

namespace UniTask
{
    public interface ISchedulable
    {
        /// <returns>実行が終了したか？Coroutineの実行が一回で終わらない場合がある</returns>
        ExecutionStatus Execute();
        Exception GetError();
        IScheduler Schedulder { get; }

        ISchedulable Parent { get; set; }
        void AddChild(ISchedulable child);
        IEnumerable<ISchedulable> Traverse();
    }

    public static class ISchedulableExtensions
    {
        public static ISchedulable GetRoot(this ISchedulable self)
        {
            var current = self;
            while (current.Parent != null)
            {
                current = current.Parent;
            }
            return current;
        }
    }

    public class Schedulable<T> : ISchedulable
    {
        List<ISchedulable> m_children = new List<ISchedulable>();
        public void AddChild(ISchedulable child)
        {
            child.Parent = this;
            m_children.Add(child);
        }
        public IEnumerable<ISchedulable> Traverse()
        {
            yield return this;

            foreach (var child in m_children)
            {
                foreach (var x in child.Traverse())
                {
                    yield return x;
                }
            }
        }

        public ISchedulable Parent
        {
            get;
            set;
        }

        public IScheduler Schedulder
        {
            get;
            private set;
        }

        public IFunctor<T> Func
        {
            get;
            private set;
        }

        public Exception GetError()
        {
            return Func.GetError();
        }

        public Schedulable()
        {
        }

        public Schedulable(IScheduler scheduler, IFunctor<T> func)
        {
            Schedulder = scheduler;
            Func = func;
        }

        public ExecutionStatus Execute()
        {
            if (Func == null)
            {
                return ExecutionStatus.Done;
            }
            return Func.Execute();
        }

        public Schedulable<Unit> AddTask(IScheduler scheduler, Action pred)
        {
            return AddTask(scheduler, () => { pred(); return Unit.Default; });
        }

        public Schedulable<U> AddTask<U>(IScheduler scheduler, Func<U> pred)
        {
            var schedulable = new Schedulable<U>(scheduler, Functor.Create(() => Unit.Default, _ => pred()));
            AddChild(schedulable);
            return schedulable;
        }

        public Schedulable<T> AddCoroutine(IScheduler scheduler, Func<IEnumerator> starter)
        {
            var func = CoroutineFunctor.Create(() => default(T), _ => starter());
            var schedulable = new Schedulable<T>(scheduler, func);
            AddChild(schedulable);
            return schedulable;
        }

        public Schedulable<Unit> ContinueWith(IScheduler scheduler, Action<T> pred)
        {
            return ContinueWith(scheduler, t => { pred(t); return Unit.Default; });
        }

        public Schedulable<U> ContinueWith<U>(IScheduler scheduler, Func<T, U> pred)
        {
            Func<T> getResult = null;
            if (Func != null)
            {
                getResult = Func.GetResult;
            }
            var func = Functor.Create(getResult, pred);
            var schedulable = new Schedulable<U>(scheduler, func);
            Parent.AddChild(schedulable);
            return schedulable;
        }

        public Schedulable<T> ContinueWithCoroutine(IScheduler scheduler, Func<IEnumerator> starter)
        {
            var func = CoroutineFunctor.Create(() => default(T), _ => starter());
            var schedulable = new Schedulable<T>(scheduler, func);
            Parent.AddChild(schedulable);
            return schedulable;
        }

        public Schedulable<Unit> OnExecute(IScheduler scheduler, Action<Schedulable<Unit>> pred)
        {
            Func<T> getResult = null;
            if (Func != null)
            {
                getResult = Func.GetResult;
            }

            var schedulable = new Schedulable<Unit>();
            schedulable.Func = Functor.Create(getResult, _ => { pred(schedulable); return Unit.Default; });
            Parent.AddChild(schedulable);
            return schedulable;
        }

        /*
        public ISchedulable<U> ContinueWithNested<U>(Func<T, ISchedulable<U>> starter, IScheduler scheduler)
        {
            var func = SchedulableFunctor.Create(() => starter(Func.GetResult()));
            return new Schedulable<U>(scheduler, func, this);
        }
        */
    }

    public static class Schedulable
    {
        public static Schedulable<Unit> Create()
        {
            return new Schedulable<Unit>();
        }
    }

    public static class SchedulableExtensions
    {
        public static void Subscribe<T>(
            this Schedulable<T> schedulable,
            IScheduler scheduler,
            Action<T> onCompleted,
            Action<Exception> onError)
        {
            schedulable.ContinueWith(scheduler, onCompleted);
            TaskChain.Schedule(schedulable.GetRoot(), onError);
        }

#if (NET_4_6 && UNITY_2018_1_OR_NEWER)
        public static Task<T> ToTask<T>(this Schedulable<T> schedulable)
        {
            return ToTask(schedulable, Scheduler.MainThread);
        }

        public static Task<T> ToTask<T>(this Schedulable<T> schedulable, IScheduler scheduler)
        {
            var tcs = new TaskCompletionSource<T>();
            schedulable.Subscribe(scheduler, r => tcs.SetResult(r), ex => tcs.SetException(ex));
            return tcs.Task;
        }
#endif

        /*
        public static ISchedulable<Unit> Sequencial<T>(IEnumerable<ISchedulable<T>> schedulables, Action<T> mergePred)
        {
            var it = schedulables.GetEnumerator();
            ISchedulable<Unit> last = Schedulable.Start(null, () => Unit.Default);
            while (it.MoveNext())
            {
                var current = it.Current;
                var merger = current.ContinueWith(result =>
                {
                    mergePred(result);
                    return Unit.Default;
                });

                if (last != null)
                {
                    // 連結
                    current.EnumParents().Last().Parent = last;
                }

                last = merger;
            }

            return last;
        }
        */

        /*
        public static ISchedulable<S> MergeCollection<S, T, U>(this ISchedulable<S> schedulable,
            Func<S, IEnumerable<T>> extractor,
            Func<S, T, U> pred,
            Action<S, U> merger,
            IScheduler scheduler = null)
        {
            return schedulable.ContinueWithNested(x =>
            {
                var schedulables = extractor(x).Select(y => Schedulable.Start(scheduler, () => pred(x, y)));
                return Sequencial(schedulables, z => merger(x, z)).ContinueWith(_ => x);
            }, scheduler);
        }
        */
    }
}

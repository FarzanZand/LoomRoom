using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Steps a coroutine and every routine it nests itself, catching what they throw. Unity never resumes (nor disposes) a
// coroutine whose nested routine threw, so a try/finally around a plain `yield return Nested()` never runs on an
// error. Through this, an error is logged, every nested routine's finally runs (they are disposed), and failed is
// called, so the caller can put things right.
public static class GuardedRoutine
{
    public static IEnumerator Run(IEnumerator routine, Action<Exception> failed = null)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        while (stack.Count > 0)
        {
            object current = null;
            bool advanced = false;
            Exception error = null;
            try { advanced = stack.Peek().MoveNext(); if (advanced) current = stack.Peek().Current; }
            catch (Exception e) { error = e; }
            if (error != null)
            {
                Debug.LogException(error);
                while (stack.Count > 0)
                {
                    try { (stack.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                failed?.Invoke(error);
                yield break;
            }
            if (!advanced) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
    }
}

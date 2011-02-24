using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading;

namespace SimpleQueue
{
    public class InProcQueue : BaseQueue
    {
        public InProcQueue()
        {
            InProcServer.AddRef();
        }


        public override ISendChannel CreatePub(string aName)
        {
            return new InProcSend(InProcServer.GetListener(aName));
        }

        public override IReceiveChannel CreateSub(string aName)
        {
            return new InProcReceive(this, aName);
        }

        public override void Dispose()
        {
            InProcServer.RemoveRef();
        }

    }
    internal static class InProcServer
    {
        private static ReaderWriterLock fLock;
        private static Dictionary<string, InProcListener> fQueue;
        private static int fRefCount;
        private static System.Timers.Timer fTimer;

        static InProcServer()
        {
            fQueue = new Dictionary<string, InProcListener>();
            fLock = new ReaderWriterLock();
        }


        public static InProcListener AddListener(string aPath, Action<object> aListener)
        {
            InProcListener Result;
            fLock.AcquireWriterLock(0x7fffffff);
            try
            {
                if (!fQueue.TryGetValue(aPath, out Result))
                {
                    Result = new InProcListener(aPath);
                    fQueue.Add(aPath, Result);
                }
            }
            finally
            {
                fLock.ReleaseWriterLock();
            }
            lock (Result)
                Result.AddLast(aListener);

            return Result;
        }

        public static void AddRef()
        {
            if (Interlocked.Increment(ref fRefCount) == 1)
            {
                fTimer = new System.Timers.Timer();
                fTimer.Elapsed += new ElapsedEventHandler(InProcServer.fTimer_Elapsed);
                fTimer.Interval = 0x1d4c0;
                fTimer.AutoReset = true;
                fTimer.Enabled = true;
            }
        }





        public static void Cleanup()
        {
            fLock.AcquireWriterLock(Int32.MaxValue);
            try
            {
                var lToDelete = new LinkedList<String>();
                foreach (var el in fQueue)
                    if (el.Value.RefCount == 0 && el.Value.Count == 0)
                        lToDelete.AddLast(el.Key);


                foreach (var el in lToDelete)
                    fQueue.Remove(el);
            }
            finally
            {

                fLock.ReleaseWriterLock();
            }
        }
        private static void fTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Cleanup();

        }
        public static InProcListener GetListener(string aPath)
        {
            InProcListener Result;
            fLock.AcquireReaderLock(0x7fffffff);
            try
            {
                if (!fQueue.TryGetValue(aPath, out Result))
                {
                    LockCookie lCookie = fLock.UpgradeToWriterLock(0x7fffffff);
                    try
                    {
                        if (!fQueue.TryGetValue(aPath, out Result))
                        {
                            Result = new InProcListener(aPath);
                            fQueue.Add(aPath, Result);
                        }
                    }
                    finally
                    {
                        fLock.DowngradeFromWriterLock(ref lCookie);
                    }
                }
                Result.AddRef();
            }
            finally
            {
                fLock.ReleaseReaderLock();
            }
            return Result;

        }
        public static void ReleaseListener(InProcListener aRef)
        {
            fLock.AcquireReaderLock(0x7fffffff);
            try
            {
                aRef.RemoveRef();
            }
            finally
            {
                fLock.ReleaseReaderLock();
            }

        }
        public static void RemoveListener(string aPath, Action<object> aListener)
        {
            InProcListener lWork;
            fLock.AcquireWriterLock(0x7fffffff);
            try
            {
                if (!fQueue.TryGetValue(aPath, out lWork))
                {
                    lWork = new InProcListener(aPath);
                    fQueue.Add(aPath, lWork);
                }
            }
            finally
            {
                fLock.ReleaseWriterLock();
            }
            lock (lWork)
                lWork.Remove(aListener);

        }
        public static void RemoveRef()
        {
            if (Interlocked.Decrement(ref fRefCount) == 0)
            {
                if (fTimer is IDisposable)
                {
                    fTimer.Dispose();
                }
                fTimer = null;
                Cleanup();
            }

        }
    }

    internal class InProcListener : LinkedList<Action<object>>
    {
        private readonly string fName;
        private volatile int fCount;

        public InProcListener(string aName)
        {
            fName = aName;
        }
#pragma warning disable 0420
        public void AddRef()
        {
            Interlocked.Increment(ref fCount);
        }
        public void RemoveRef()
        {
            Interlocked.Decrement(ref fCount);
        }
#pragma warning restore 0420

        public string Name { get { return fName; } }
        public int RefCount { get { return fCount; } }
    }

    internal class InProcReceive : BaseReceiveChannel
    {
        private InProcListener fChannel;
        private LinkedList<object> fMessages;
        private InProcQueue fOwner;

        public override event Action HaveData;

        public InProcReceive(InProcQueue aOwner, string aName)
        {
            this.fMessages = new LinkedList<object>();
            this.fOwner = aOwner;
            this.fChannel = InProcServer.AddListener(aName, new Action<object>(this.Callback));
        }

        public void Callback(object data)
        {
            lock (fMessages) 
                fMessages.AddLast(data);
            Action lcb = HaveData;
            if (lcb != null)
            {
                this.fOwner.InvokeCallback(lcb);
            }

        }
        public override void Dispose()
        {
             InProcServer.RemoveListener(this.fChannel.Name, new Action<object>(this.Callback));
}


        public override byte[] ReceiveBytes()
        {
            lock (fMessages)
            {
                return (byte[])fMessages.FirstOrDefault();
            }

        }
        public override object ReceiveObject()
        {
            lock (fMessages)
            {
                return fMessages.FirstOrDefault();
            }
        }
        public override string ReceiveString()
        {
            lock (fMessages)
            {
                return (string)fMessages.FirstOrDefault();
            }
        }
        public override void TriggerExisting()
        {
            Action lCB = HaveData;
            if (lCB != null)
            {
                while (true)
                {
                    lock (fMessages)
                        if (fMessages.Count == 0) return;
                    lCB();
                }
            }

        }


        public override string Name { get { return fChannel.Name; } }
    }

    internal class InProcSend : BaseSendChannel
    {
        private InProcListener fChannel;

        public InProcSend(InProcListener aChannel)
        {
            this.fChannel = aChannel;

        }
        public override void Dispose()
        {
            InProcServer.ReleaseListener(this.fChannel);
            this.fChannel = null;

        }
        public override void Send(byte[] data)
        {
            Send((object)data);

        }
        public override void Send(object data)
        {
            LinkedListNode<Action<Object>> lItem;
            lock (fChannel)
            {
                lItem = fChannel.First;
            }
            while (lItem != null)
            {
                lItem.Value(data);
                lock (fChannel)
                {
                    lItem = lItem.Next;
                }
            }
        }
        

        public override void Send(string data)
        {
            Send((object)data);

        }

        public override string Name
        {
            get
            {
                return fChannel.Name;
            }
        }
    }

 




}

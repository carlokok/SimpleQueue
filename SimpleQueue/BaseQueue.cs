using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SimpleQueue
{
    public abstract class BaseQueue : IDisposable
    {
        protected BaseQueue()
        {
            InThreadPool = true;
        }
        public abstract ISendChannel CreatePub(string aName);
        public abstract IReceiveChannel CreateSub(string aName);
        public virtual void Dispose()
        {
        }
        protected internal virtual void InvokeCallback(Action cb)
        {
            if (this.InThreadPool)
            {
                ThreadPool.QueueUserWorkItem(delegate(object a)
                {
                    (a as Action)();
                }, cb);
            }
            else
            {
                cb();
            }

        }

        public bool InThreadPool { get; set; }
    }


    public interface IChannel
    {
        string Name { get; }
    }


    public interface IReceiveChannel : IChannel, IDisposable
    {
        event Action HaveData;

        byte[] ReceiveBytes();
        object ReceiveObject();
        string ReceiveString();
    }



    public interface ISendChannel : IChannel, IDisposable
    {
        void Send(byte[] data);
        void Send(object data);
        void Send(string data);
    }

}

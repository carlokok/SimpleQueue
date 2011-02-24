using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace SimpleQueue
{
    public abstract class BaseSendChannel : ISendChannel
    {
        private BinaryFormatter fBinFormatter;
        private MemoryStream fBinMsg;

        protected BaseSendChannel() { }
        public virtual void Dispose() { }
        public virtual void Send(object data)
        {
            if (this.fBinFormatter == null)
            {
                this.fBinFormatter = new BinaryFormatter();
                this.fBinMsg = new MemoryStream();
            }
            this.fBinMsg.SetLength(0L);
            this.fBinFormatter.Serialize(this.fBinMsg, data);
            this.Send(this.fBinMsg.ToArray());
        }


        public virtual void Send(string data)
        {
            this.Send(Encoding.UTF8.GetBytes(data));
        }

        public abstract void Send(byte[] data);

        public abstract string Name { get; }
    }

 

}

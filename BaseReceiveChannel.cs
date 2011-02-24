using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace SimpleQueue
{
    public abstract class BaseReceiveChannel : IReceiveChannel
    {
        private BinaryFormatter fBinFormatter;
        private MemoryStream fBinMsg;

        public abstract event Action HaveData;

        protected BaseReceiveChannel() { }
        public virtual void Dispose() { }
        public abstract byte[] ReceiveBytes();
        public virtual object ReceiveObject()
        {
            if (this.fBinFormatter == null)
            {
                this.fBinFormatter = new BinaryFormatter();
                this.fBinMsg = new MemoryStream();
            }
            this.fBinMsg.SetLength(0L);
            byte[] lData = this.ReceiveBytes();
            this.fBinMsg.Write(lData, 0, lData.Length);
            this.fBinMsg.Position = 0L;
            return this.fBinFormatter.Deserialize(this.fBinMsg);
        }

        public virtual string ReceiveString()
        {
            return Encoding.UTF8.GetString(this.ReceiveBytes());
        }

        public abstract void TriggerExisting();

        public abstract string Name { get; }
    }

 

}

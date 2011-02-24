using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;

namespace SimpleQueue
{
    public class StompQueue : BaseQueue
    {

        internal StompClient fClient;
        internal bool fConnected;
        internal ConcurrentDictionary<string, StompReceiveChannel> fReceivers;

        public StompQueue()
        {
            this.fReceivers = new ConcurrentDictionary<string, StompReceiveChannel>();
            this.Port = 0xf0ad;
            this.PubSubPath = "/topic/{0}";
            this.ContentType = "application/octet-stream";
        }





        public void Connect()
        {
            if (this.fConnected)
            {
                this.Disconnect();
            }
            this.fClient = new StompClient(this.Target, this.Port, Username, Password);
            this.fClient.HaveMessage += new Action<StompMessage>(this.fClient_HaveMessage);
            this.fClient.StartReadMessage();
            this.fConnected = true;
        }
        public override ISendChannel CreatePub(string aName)
        {
            if (!this.fConnected)
            {
                this.Connect();
            }
            return new StompSendChannel(this, aName);
        }

        public override IReceiveChannel CreateSub(string aName)
        {
            StompQueue Self = this;
            if (!this.fConnected)
            {
                this.Connect();
            }
            string lID = Guid.NewGuid().ToString("N");
            IReceiveChannel Result = this.fReceivers.GetOrAdd(lID, a => new StompReceiveChannel(Self, aName, lID));
            lock (fClient)
            {
                fClient.Subscribe(string.Format(this.PubSubPath, aName), lID, false);
            }
            return Result;
        }





        public void Disconnect()
        {
            if (this.fClient is IDisposable)
            {
                this.fClient.Dispose();
            }
            this.fClient = null;
            this.fConnected = false;
        }





        internal void fClient_HaveMessage(StompMessage obj)
        {
            if (obj.TypeStr == "MESSAGE")
            {
                StompReceiveChannel lChannel;
                StringPair lID = (from a in obj.Headers
                                                    where a.Key == "subscription"
                                                    select a).FirstOrDefault<StringPair>();
                if ((lID != null) && this.fReceivers.TryGetValue(lID.Value, out lChannel))
                {
                    lChannel.DataAvailable(obj.Data);
                }
            }

        }

        public bool Connected { get { return this.fConnected; } }
        public string ContentType { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
        public string PubSubPath { get; set; }
        public string Target { get; set; }
        public string Username { get; set; }
    }
    public class StompSendChannel : BaseSendChannel
    {
        private string fName;
        private StompQueue fOwner;

        public StompSendChannel(StompQueue aOwner, string aName)
        {
            fName = aName;
            fOwner = aOwner;
        }
        public override void Send(byte[] data)
        {
            List<StringPair> lHeaders = new List<StringPair>();
            if (this.fOwner.ContentType != null)
            {
                lHeaders.Add(new StringPair("content-type", fOwner.ContentType));
            }
            using (this.fOwner.fClient)
            {
                this.fOwner.fClient.Send(string.Format(this.fOwner.PubSubPath, this.fName), null, null, data, lHeaders);

            }
        }

        public override string Name { get { return fName; } }
    }



    public class StompReceiveChannel : BaseReceiveChannel
    {
        private LinkedList<byte[]> fData;
        private string fID;
        private string fName;
        private StompQueue fOwner;

        public override event Action HaveData;

        public StompReceiveChannel(StompQueue aOwner, string aName, string aID)
        {
            this.fData = new LinkedList<byte[]>();
            this.fOwner = aOwner;
            this.fID = aID;
            this.fName = aName;

        }
        internal void DataAvailable(byte[] aData)
        {
            LinkedList<byte[]> wv = this.fData;
            lock (wv)
            {
                fData.AddLast(aData);
            }

            Action lCB = HaveData;
            if (lCB != null)
            {
                this.fOwner.InvokeCallback(lCB);
            }

        }
        public override void Dispose()
        {
            StompReceiveChannel res;
            lock (fOwner.fClient)
            {
                fOwner.fClient.Unsubscribe(null, this.fID);

            }
            fOwner.fReceivers.TryRemove(this.fID, out res);
        }
        public override byte[] ReceiveBytes()
        {
            lock (fData)
            {
                if (fData.Count > 0)
                {
                    var el = fData.Last.Value;
                    fData.RemoveLast();
                    return el;
                }
            }
            return null;
        }
        public override void TriggerExisting()
        {
            Action lcb = this.HaveData;
            if (lcb != null)
            {
                while (true)
                {
                    lock (fData)
                    {
                        if (fData.Count == 0)
                            return;
                    }
                    lcb();
                }
            }

        }

        public string ID { get { return fID; } }
        public override string Name { get { return fName; } }
    }



}

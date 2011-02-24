using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using RemObjects.InternetPack;

namespace SimpleQueue
{

    public class StringPair
    {
        public readonly string Key;
        public readonly string Value;
        public StringPair(string key, string value)
        {
            this.Key = key;
            this.Value = value;
        }
    }
    public class StompMessage
    {
        public byte[] Data { get; set; }
        public IList<StringPair> Headers { get; set; }
        public string TypeStr { get; set; }
    }




    public class StompClient : IDisposable
    {
        private MemoryStream fBuilder;
        private byte[] fByteReader;
        private StringBuilder fSBuilder;
        private string fSessionID;
        private Connection fSocket;

        public event Action<StompMessage> HaveMessage;

        public StompClient(string aTargetHost, [DefaultParameterValue(0xf0ad)] int aPort, string aUsername, string aPassword)
        {
            this.fSBuilder = new StringBuilder();
            this.fBuilder = new MemoryStream();
            this.fByteReader = new byte[1];

            fSocket = new Connection(new Binding());
            var lEntry = System.Net.Dns.GetHostEntry(aTargetHost);

            for (int i = 0; i < lEntry.AddressList.Length; i++)
            {
                var lIP = lEntry.AddressList[i];
                try
                {
                    fSocket.Connect(lIP, aPort);

                    break;
                }
                catch { }
            }
            if (!fSocket.Connected) throw new ArgumentException("No such host");

            Send("CONNECT", new StringPair[] { new StringPair("login", aUsername), new StringPair("passcode", aPassword) }, null);
            string lResp;
            IList<StringPair> lHeaders;
            byte[] lBody;

            Receive(out lResp, out lHeaders, out lBody);
            if (lResp != "CONNECTED") throw new ArgumentException("Could not login: " + TryTurnIntoError(lResp, lBody));
            if (lHeaders != null)
                fSessionID = lHeaders.Where(a => a.Key == "session").Select(a => a.Value).FirstOrDefault();
        }

        public void AbortTransaction(string aTransaction)
        {
            List<StringPair> lList = new List<StringPair> {
        new StringPair("transaction", aTransaction)
    };
            this.Send("ABORT", lList, null);

        }
        public void Ack(string aMessageId, [DefaultParameterValue(null)] string aTransaction)
        {
            List<StringPair> lList = new List<StringPair> {
        new StringPair("message-id", aMessageId)
    };
            if (aTransaction != null)
            {
                lList.Add(new StringPair("transaction", aTransaction));
            }
            this.Send("ACK", lList, null);

        }
        public void BeginTransaction(string aTransaction)
        {
            List<StringPair> lList = new List<StringPair> {
        new StringPair("transaction", aTransaction)
    };
            this.Send("BEGIN", lList, null);

        }
        public void CommitTransaction(string aTransaction)
        {
            List<StringPair> lList = new List<StringPair> {
        new StringPair("transaction", aTransaction)
    };
            this.Send("COMMIT", lList, null);

        }
        public void Dispose()
        {
            this.Send("DISCONNECT", null, null);
            if (this.fSocket is IDisposable)
            {
                this.fSocket.Dispose();
            }
            this.fSocket = null;
        }

        private void GotAByte(IAsyncResult ar)
        {
            try
            {
                if (fSocket.EndRead(ar) == 0) return;
                var lGotNull = false;
                var lCommand = (StompMessage)ar.AsyncState;
                if (fByteReader[0] == 0)
                    lGotNull = true;
                else
                {
                    while (fSocket.Available > 0)
                    {
                        var data = fSocket.ReadByte();
                        if (data == -1) return;
                        if (data == 0)
                        {
                            lGotNull = true;
                            break;
                        }
                        fBuilder.WriteByte((byte)data);
                    }
                }

                if (lGotNull)
                {
                    lCommand.Data = fBuilder.ToArray();
                    if (HaveMessage != null)
                        HaveMessage(lCommand);
                    fSocket.BeginReadLine(new AsyncCallback(GotCommand), null);
                }
                else
                    fSocket.BeginRead(fByteReader, 0, 1, @GotAByte, lCommand);
            }
            catch { }
        }
        private void GotCommand(IAsyncResult ar)
        {
            try
            {
                string res = this.fSocket.EndReadLine(ar);
                if (res != null)
                {
                    StompMessage lCommand = new StompMessage
                    {
                        TypeStr = res,
                        Headers = new List<StringPair>()
                    };
                    this.fSocket.BeginReadLine(new AsyncCallback(this.GotHeader), lCommand);
                }
            }
            catch
            {
            }
        }



        private void GotData(IAsyncResult ar)
        {
            try
            {
                StompMessage lCommand = ar.AsyncState as StompMessage;
                if (this.fSocket.EndRead(ar) == lCommand.Data.Length)
                {
                    this.fSocket.BeginRead(this.fByteReader, 0, 1, new AsyncCallback(this.GotZeroByte), lCommand);
                }
            }
            catch
            {
            }
        }





        private void GotHeader(IAsyncResult ar)
        {
            try
            {
                var res = fSocket.EndReadLine(ar);
                if (res == null) return; // eof
                var lCommand = (StompMessage)ar.AsyncState;
                if (res == "")
                {
                    Int64 ctl = -1;
                    var lContentLength = lCommand.Headers.FirstOrDefault(a => a.Key == "content-length");
                    if (lContentLength != null)
                        Int64.TryParse(lContentLength.Value, out ctl);
                    if (ctl == -1)
                    {
                        fBuilder.SetLength(0);
                        var lGotNull = false;
                        while (fSocket.Available > 0)
                        {
                            var data = fSocket.ReadByte();
                            if (data == -1) return;
                            if (data == 0) { lGotNull = true; break; }
                            fBuilder.WriteByte((byte)data);
                        }
                        if (lGotNull)
                        {
                            lCommand.Data = fBuilder.ToArray();
                            if (HaveMessage != null)
                                HaveMessage(lCommand);
                            fSocket.BeginReadLine(@GotCommand, null);
                        }
                        else
                            fSocket.BeginRead(fByteReader, 0, 1, @GotAByte, lCommand);
                    }
                    else
                    {
                        if (ctl == 0)
                            fSocket.BeginRead(fByteReader, 0, 1, @GotZeroByte, lCommand);

                        lCommand.Data = new Byte[ctl];
                        fSocket.BeginRead(lCommand.Data, 0, lCommand.Data.Length, @GotData, lCommand);
                    }
                }
                else
                {
                    var lHeader = res.Split(new char[] { ':' }, 2);
                    if (lHeader.Length == 2)
                    {
                        lHeader[1] = lHeader[1].TrimStart();
                        lCommand.Headers.Add(new StringPair(lHeader[0], lHeader[1]));
                    }
                    fSocket.BeginReadLine(@GotHeader, lCommand);
                }
            }
            catch { }
        }
        private void GotZeroByte(IAsyncResult ar)
        {
            try
            {
                StompMessage lCommand = ar.AsyncState as StompMessage;
                if (!((this.fSocket.EndRead(ar) == 1) ? (this.fByteReader[0] != 0) : true))
                {
                    if (this.HaveMessage != null)
                    {
                        this.HaveMessage(lCommand);
                    }
                    this.fSocket.BeginReadLine(new AsyncCallback(this.GotCommand), null);
                }
            }
            catch
            {
            }
        }





        protected void Receive(out string aResponse, out IList<StringPair> aHeaders, out byte[] aBody)
        {
            aResponse = fSocket.ReadLine();
            if (aResponse == null) throw new ObjectDisposedException("socket");
            aHeaders = new List<StringPair>();
            Int64 ctl = -1;
            while (true)
            {
                var lResp = fSocket.ReadLine();
                if (lResp == null) throw new ObjectDisposedException("socket");
                if (lResp == "") break;
                var lHeader = lResp.Split(new char[] { ':' }, 2);
                if (lHeader.Length == 2)
                {
                    lHeader[1] = lHeader[1].TrimStart();
                    aHeaders.Add(new StringPair(lHeader[0], lHeader[1]));
                    if (String.Equals(lHeader[0], "content-length"))
                        Int64.TryParse(lHeader[1], out ctl);
                }
            }
            if (ctl == -1)
            {
                fBuilder.SetLength(0);

                while (true)
                {
                    var lCode = fSocket.ReadByte();
                    if (lCode == -1) throw new ObjectDisposedException("socket");
                    if (lCode == 0) break;
                    fBuilder.WriteByte((byte)lCode);
                }
                aBody = fBuilder.ToArray();
            }
            else
            {
                aBody = new Byte[ctl];

                var lIndex = 0;
                while (lIndex < aBody.Length)
                {
                    var lRes = fSocket.Read(aBody, lIndex, aBody.Length - lIndex);
                    if (lRes == 0) throw new ObjectDisposedException("socket");
                    lIndex = lIndex + lRes;
                }
                var lCode = fSocket.ReadByte();
                if (lCode != 0) throw new ArgumentException("Protocol error");
            }
        }
        protected void Send(string aCommand, IList<StringPair> aHeaders, byte[] aBody)
        {
            fSBuilder.Length = 0;
            fSBuilder.Append(aCommand);
            fSBuilder.Append("\r\n");
            for (int i = 0; i < (aHeaders == null ? 0 : aHeaders.Count); i++)
            {
                fSBuilder.AppendFormat("{0}: {1}\r\n", aHeaders[i].Key, aHeaders[i].Value);
            }
            if (aBody != null && aBody.Length != 0)
            {
                fSBuilder.AppendFormat("content-length: {0}\r\n", aBody.Length);
            }
            fSocket.WriteLine(fSBuilder.ToString());
            if (aBody != null)
            {
                fSocket.Write(aBody, 0, aBody.Length);
            }
            fSocket.WriteByte(0);
        }
        public void Send(string aTarget, [DefaultParameterValue(null)] string aWantReceipt, [DefaultParameterValue(null)] string aTransaction, byte[] aBody, IList<StringPair> aHeaders)
        {
            List<StringPair> lList = aHeaders as List<StringPair>;
            if (lList == null)
            {
                lList = new List<StringPair>();
                if (aHeaders != null)
                {
                    lList.AddRange(aHeaders);
                }
            }
            lList.Add(new StringPair("destination", aTarget));
            if (!string.IsNullOrEmpty(aWantReceipt))
            {
                lList.Add(new StringPair("receipt", aWantReceipt));
            }
            if (!string.IsNullOrEmpty(aTransaction))
            {
                lList.Add(new StringPair("transaction", aTransaction));
            }
            this.Send("SEND", lList, aBody);
        }





        public void StartReadMessage()
        {
            try
            {
                this.fSocket.BeginReadLine(new AsyncCallback(this.GotCommand), null);
            }
            catch
            {
            }
        }





        public void Subscribe(string aDestination, [DefaultParameterValue(null)] string aID, [DefaultParameterValue(false)] bool aClientAcknowledge)
        {
            List<StringPair> lList = new List<StringPair> {
        new StringPair("destination", aDestination)
    };
            if (aClientAcknowledge)
            {
                lList.Add(new StringPair("ack", "client"));
            }
            else
            {
                lList.Add(new StringPair("ack", "auto"));
            }
            if (!string.IsNullOrEmpty(aID))
            {
                lList.Add(new StringPair("id", aID));
            }
            this.Send("SUBSCRIBE", lList, null);
        }





        private string TryTurnIntoError(string lResp, byte[] lBody)
        {
            if (lResp == "ERROR")
            {
                return ("Error: " + Encoding.UTF8.GetString(lBody));
            }
            return ("Unknown response: " + lResp);
        }





        public void Unsubscribe(string aDestination, [DefaultParameterValue(null)] string aID)
        {
            List<StringPair> lList = new List<StringPair>();
            if (aDestination != null)
            {
                lList.Add(new StringPair("destination", aDestination));
            }
            if (!string.IsNullOrEmpty(aID))
            {
                lList.Add(new StringPair("id", aID));
            }
            this.Send("UNSUBSCRIBE", lList, null);
        }






        public string SessionID
        {
            get
            {
                return this.fSessionID;
            }
        }


    }



}

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using uPLibrary.Networking.M2Mqtt;


namespace MqttSpectrumViewer
{
    public partial class Form1 : Form
    {
        MqttClient MqttClient;
        List<MachineState> MachineStates;

        const string selectedMachineNo = "087708"; //Es werden nur Spektren von einer Maschine ausgewertet

        int spectrumcounter = 0;
        Stopwatch watch = new Stopwatch();
        double specspersecond = 0;
        double avglatency = 0;
        object lockobject = new object();

        public Form1()
        {
            InitializeComponent();

            MachineStates = new List<MachineState>();

            MqttClient = new MqttClient("192.168.0.99");
            MqttClient.Connect("SpectrumViewer2"); //Muss je Instanz eindeutig gemacht werden
            MqttClient.MqttMsgPublishReceived += MqttClient_MqttMsgPublishReceived;
            MqttClient.Subscribe(new string[] { "/+/vibration/#", "/+/machinestate/#" }, new byte[] { 0,0 });
            watch.Start();

            this.FormClosing += Form1_FormClosing;

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            MqttClient.Unsubscribe(new string[] { "/+/vibration/#", "/+/machinestate/#" });
            MqttClient.Disconnect();
            MqttClient.MqttMsgPublishReceived -= MqttClient_MqttMsgPublishReceived;
        }

        private void MqttClient_MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            if(this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler<uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs>(MqttClient_MqttMsgPublishReceived), sender, e);
                return;
            }
            string payload = Encoding.UTF8.GetString(e.Message);

            //Frequenzspektrum
            if (e.Topic.IndexOf("/vibration/") > -1 && e.Topic.IndexOf("/spectrum") > -1)
            {
                string machineNo = "";
                var items = e.Topic.Split('/');
                foreach (var i in items)
                {
                    if (String.IsNullOrEmpty(i))
                        continue;
                    machineNo = i;
                    break;
                }
                
                if (machineNo == selectedMachineNo)
                {
                    JsonSpec receivedspectrum = JsonConvert.DeserializeObject<JsonSpec>(payload);
                    receivedspectrum.topic = e.Topic;
                    NewSpectrum(sender, receivedspectrum.spectrum);
                }
            }
            //Zeitkennwerte
            if (e.Topic.IndexOf("/vibration/") > -1 && e.Topic.IndexOf("/timevalues") > -1)
            {
                JsonTimeValues receivedtimevalues = JsonConvert.DeserializeObject<JsonTimeValues>(payload);
                receivedtimevalues.topic = e.Topic;
                var items = e.Topic.Split('/');
                string machineNo = "";
                foreach (var i in items)
                {
                    if (String.IsNullOrEmpty(i))
                        continue;
                    machineNo = i;
                    break;
                }

                bool itemFound = false;
                foreach (var ms in MachineStates)
                {
                    if (ms.MachineNo == machineNo)
                    {
                        itemFound = true;
                        ModifyMachineState(ms, "VibrationRMS", receivedtimevalues.timevalues.rms.ToString("0.0"));
                    }
                }
            }
            //Maschinenstatus
            if (e.Topic.IndexOf("/machinestate")>-1)
            {
                var items = e.Topic.Split('/');
                string machineNo = "";
                foreach(var i in items)
                {
                    if (String.IsNullOrEmpty(i))
                        continue;
                    machineNo = i;
                    break;
                }
                bool itemFound = false;
                foreach (var ms in MachineStates)
                {
                    if (ms.MachineNo == machineNo)
                    {
                        itemFound = true;
                        ModifyMachineState(ms, items[items.Length - 1], payload);
                    }
                }
                if(!itemFound)
                {
                    MachineState ms = new MachineState();
                    ms.MachineNo = machineNo;
                    ModifyMachineState(ms, items[items.Length - 1], payload);
                    MachineStates.Add(ms);
                }
                
            }
        }
        


        void ModifyMachineState(MachineState ms, string topic, string payload)
        {
            switch (topic)
            {
                case "CVel":
                    ms.Drehzahl = Convert.ToInt32(payload);
                    break;
                case "NCProgSub":
                    ms.NCProgSub = Convert.ToInt32(payload);
                    break;
                case "Handling":
                    ms.Handling = Convert.ToInt32(payload);
                    break;
                case "ProcStep":
                    ms.ProcStep = Convert.ToInt32(payload);
                    break;
                case "PartName":
                    ms.PartName = payload;
                    break;
                case "DresGearCnt":
                    ms.DresGearCnt = Convert.ToInt32(payload);
                    break;
                case "TotalPartCount":
                    ms.TotalPartCnt = Convert.ToInt32(payload);
                    break;
                case "VibrationRMS":
                    ms.VibrationRMS = Convert.ToDouble(payload);
                    break;
            }

    }

        
        void NewSpectrum(object sender, spectrum s)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new EventHandler<spectrum>(NewSpectrum), sender, s);
                return;
            }
           
                var localTimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var timediffms = localTimeStamp - s.timestamp;
                lbl_Latency.Text = timediffms.ToString() + " ms Latenz";
                spectrumcounter++;
                avglatency += timediffms;

               
            if(spectrumcounter>=100)
            {
                lock (lockobject)
                {
                    avglatency /= spectrumcounter;
                    specspersecond = 1000.0 * spectrumcounter / watch.ElapsedMilliseconds;
                    lbl_SpecsPerSecond.Text = "Spektren / s: " + specspersecond.ToString();
                    //lbl_Latency.Text = "Avg Latency : " + avglatency.ToString();
                    SaveMeasuredPerformance(specspersecond, avglatency);
                    watch.Restart();
                    avglatency = 0;
                    spectrumcounter = 0;
                }
            }

            chart1.Series[s.sensor].Points.Clear();
            for(int i=0; i<s.values.Length; i++)
            {
                chart1.Series[s.sensor].Points.AddXY(i * s.frequ_step, s.values[i]);
            }

        }

        void SaveMeasuredPerformance(double specspers, double latencyavg)
        {
            string tsnow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            File.AppendAllText("perflog.txt",tsnow+";"+ specspers.ToString() + ";" + latencyavg.ToString()+Environment.NewLine);
        }

        //Die Klasse zum Serialisieren/Deserialisieren JSON<->Object
        public class JsonSpec
        {
            public spectrum spectrum { get; set; }
            [System.Xml.Serialization.XmlIgnore]
            internal string topic { get; set; }
            public JsonSpec()
            {
                spectrum = new spectrum();
            }
        }

        //Die eigentliche Spektrum Klasse
        public class spectrum
        {
            public long timestamp { get; set; }
            public int sensor { get; set; }
            public double frequ_step { get; set; }
            public uint[] values { get; set; }

            public spectrum()
            {
                //values = new uint[850];
            }
        }

        public class JsonTimeValues
        {
            public timevalues timevalues { get; set; }
            [System.Xml.Serialization.XmlIgnore]
            internal string topic { get; set; }
            public JsonTimeValues()
            {
                timevalues = new timevalues();
            }
        }

        public class timevalues
        {
            public long timestamp { get; set; }
            public int sensor { get; set; }
            public double rms { get; set; }
            public double peak { get; set; }
        }

        //Puffer für Spektren
        
        
        public class MachineState 
        {
            private int nCProgSub;
            private int drehzahl;
            private string machineNo;
            private int handling;
            private int procStep;
            private int dresGearCnt;
            private int totalPartCnt;
            private double vibrationRMS;
            private string partName;

            public int NCProgSub { get => nCProgSub; set { nCProgSub = value;  } }
            public int Drehzahl { get => drehzahl; set { drehzahl = value; } }
            public string MachineNo { get => machineNo; set { machineNo = value;  } }
            public int Handling { get => handling; set { handling = value; } }
            public int DresGearCnt { get => dresGearCnt; set { dresGearCnt = value;  } }
            public int TotalPartCnt { get => totalPartCnt; set { totalPartCnt = value;  } }
            public int ProcStep { get => procStep; set {procStep = value;  } }
            public double VibrationRMS { get => vibrationRMS; set {vibrationRMS = value;  } }
            public string PartName { get => partName; set { partName = value;  } }

          
        }



       
    }
}

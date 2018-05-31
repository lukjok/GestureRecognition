using Android.App;
using Android.Bluetooth;
using Android.OS;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using Java.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GestureRecognition
{
    [Activity(Label = "GestureRecognition", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/Theme.AppCompat.Light.NoActionBar")]
    public class MainActivity : AppCompatActivity
    {
        private static MyList<string> ListData;
        private string sequence = "";
        private int nCount = 0;

        BluetoothSocket cSocket;
        BluetoothDevice cDevice;
        BluetoothAdapter adapter = BluetoothAdapter.DefaultAdapter;

        TextView BigLetter;
        TextView Sequence;

        private RecyclerView.LayoutManager mLayoutManager;
        private static RecyclerView.Adapter mAdapter;
        private static RecyclerView mRecyclerView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.Main);

            BigLetter = FindViewById<TextView>(Resource.Id.currentGestureTextView);
            Sequence = FindViewById<TextView>(Resource.Id.currentGestureDescriptionTextView);

            BigLetter.Text = "A";
            Sequence.Text = "Initializing...";

            ListData = new MyList<string>();
            mRecyclerView = FindViewById<RecyclerView>(Resource.Id.gestureRecyclerView);
            mLayoutManager = new LinearLayoutManager(this);
            mRecyclerView.SetLayoutManager(mLayoutManager);
            mAdapter = new RecyclerAdapter(ListData, mRecyclerView);
            ListData.Adapter = mAdapter;
            mRecyclerView.SetAdapter(mAdapter);

            ConnectToDevice();
            DisplayPeriodically();
        }

        // Update data every 2 seconds
        private void DisplayPeriodically()
        {
            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    DisplayReadings();
                    Task.Delay(2000);
                }
            });
        }

        public override void OnDetachedFromWindow()
        {
            SendCommand("exit\n");
            cDevice.Dispose();
            cSocket.Close();
            adapter.Dispose();
            base.OnDetachedFromWindow();
        }

        public override void OnBackPressed()
        {
            SendCommand("exit\n");
            cDevice.Dispose();
            cSocket.Close();
            adapter.Dispose();
            base.OnBackPressed();
        }

        protected async override void OnResume()
        {
            base.OnResume();
        }

        // Gets data from device and format it to sequences, add spaces and other functions
        public void DisplayReadings()
        {
            Task dTask = Task.Factory.StartNew(async s =>
            {
                string letter = await ReadLetter();

                if (!letter.Contains("#"))
                {
                    if (!letter.Contains("nothing"))
                    {
                        if (letter.Contains("delete"))
                        {
                            letter = "";
                            sequence.Remove(sequence.Length - 1, 1);
                        }

                        if (letter.Contains("space"))
                        {
                            letter = "";
                            sequence += " ";
                        }

                        RunOnUiThread(() => Sequence.Text = sequence);
                    }
                    else
                    {
                        letter = "";
                        nCount++;
                    }

                    // If empty count is more than 3 then start new sentence
                    if (nCount > 3)
                    {
                        ListData.Add(sequence);
                        sequence = "";
                        nCount = 0;
                    }

                    RunOnUiThread(() => BigLetter.Text = letter);
                    sequence += letter;
                }
                else
                    await Task.Delay(250);

            }, null);

            Task.WaitAll(new Task[] { dTask });
        }

        // Connects to the device
        public void ConnectToDevice()
        {
            Task cTask = Task.Factory.StartNew(() =>
            {
                if (!adapter.IsEnabled)
                    adapter.Enable();

                adapter.StartDiscovery();
                ICollection<BluetoothDevice> devices = adapter.BondedDevices;
                if (devices.Count != 0)
                {
                    foreach (var device in devices)
                    {
                        if (device.Name.Contains("raspberrypi"))
                            cDevice = device;
                    }
                }

                if (cDevice != null)
                {

                    Java.Util.UUID uuid = Java.Util.UUID.FromString("94f39d29-7d6d-437d-973b-fba39e49d4ee");
                    try
                    {
                        cSocket = cDevice.CreateRfcommSocketToServiceRecord(uuid);
                        while (!cSocket.IsConnected)
                        {
                            cSocket.Connect();
                        }
                        string str;
                        while ((str = ReadLetter().Result) != "")
                        {
                            if (str.Contains("hi"))
                            {
                                RunOnUiThread(() => Snackbar.Make(Window.DecorView.RootView, "Connected!", Snackbar.LengthLong).Show());
                                SendCommand("hello");
                                break;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        RunOnUiThread(() => Snackbar.Make(Window.DecorView.RootView, "Error while connecting to the device", Snackbar.LengthLong).Show());
                    }
                }
                else
                    RunOnUiThread(() => Snackbar.Make(Window.DecorView.RootView, "Error while connecting to the device. Check bluetooth connection", Snackbar.LengthLong).Show());
            });

            Task.WaitAll(new Task[] { cTask });
        }

        // Sends control commands to the device
        public async void SendCommand(string command)
        {
            try
            {
                byte[] cc = Encoding.Default.GetBytes(command + "\n");
                await cSocket.OutputStream.WriteAsync(cc, 0, cc.Length);
            }
            catch
            {
                RunOnUiThread(() => Snackbar.Make(Window.DecorView.RootView, "Komanda neišsiųsta :(", Snackbar.LengthShort).Show());
            }
        }

        // Read predicted letter from device
        public async Task<string> ReadLetter()
        {
            try
            {
                var mReader = new InputStreamReader(cSocket.InputStream);
                var buffer = new BufferedReader(mReader);
                if (buffer.Ready())
                {
                    char[] bcommand = new char[20];
                    await buffer.ReadAsync(bcommand);
                    string letter = new string(bcommand.Where(x => x != '\0').ToArray());
                    return letter;
                }
                else
                    return "#";
            }
            catch
            {
                RunOnUiThread(() => Snackbar.Make(Window.DecorView.RootView, "Failed to get data :(", Snackbar.LengthShort).Show());
                return "";
            }
        }

        #region Recycle View

        public class MyList<T>
        {
            private List<T> mItems;

            public void Erase()
            {
                mItems = new List<T>();
            }

            public MyList()
            {
                mItems = new List<T>();
            }

            public RecyclerView.Adapter Adapter
            {
                get { return mAdapter; }
                set { mAdapter = value; }
            }

            public void Add(T item)
            {
                mItems.Add(item);

                if (Adapter != null)
                {
                    Adapter.NotifyItemInserted(Count);
                }
            }

            public void Remove(int position)
            {
                mItems.RemoveAt(position);

                if (Adapter != null)
                {
                    Adapter.NotifyItemRemoved(0);
                }
            }

            public T this[int index]
            {
                get { return mItems[index]; }
                set { mItems[index] = value; }
            }

            public int Count
            {
                get { return mItems.Count; }
            }

            public void clear()
            {
                int size = mItems.Count;
                Erase();
                mAdapter.NotifyItemRangeRemoved(0, size);
            }
        }

        public class RecyclerAdapter : RecyclerView.Adapter
        {
            private MyList<string> LIstDataRecycleView;
            private RecyclerView mRecyclerView;

            public RecyclerAdapter(MyList<string> mList, RecyclerView recyclerView)
            {
                LIstDataRecycleView = mList;
                mRecyclerView = recyclerView;
            }

            public class Loading : RecyclerView.ViewHolder
            {
                public View LoadingView { get; set; }

                public Loading(View view) : base(view)
                { }
            }

            public class ListVew : RecyclerView.ViewHolder
            {
                public View mList { get; set; }
                public TextView mItem { get; set; }

                public ListVew(View view) : base(view)
                {
                    mList = view;
                }
            }

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                View mMoodleViewList = LayoutInflater.From(parent.Context).Inflate(Resource.Layout.List, parent, false);

                TextView Item = mMoodleViewList.FindViewById<TextView>(Resource.Id.textView);
                ListVew view = new ListVew(mMoodleViewList) { mItem = Item };
                return view;
            }

            public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
            {
                ListVew myHolder = holder as ListVew;
                myHolder.mItem.Text = LIstDataRecycleView[position];
            }

            public override int ItemCount
            {
                get { return LIstDataRecycleView.Count; }
            }
        }

        #endregion
    }
}

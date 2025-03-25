using Android.App;
using Android.OS;
using Android.Widget;
using MikePhil.Charting.Charts;
using MikePhil.Charting.Data;
using MikePhil.Charting.Components;
using Android.Bluetooth;
using Java.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer; // Add this line to specify the Timer namespace


namespace BluetoothSerialCommunication.Src
{
    [Activity(Label = "通信界面")]
    public class BluetoothCommunicationActivity : Activity
    {
        private BluetoothDevice? _device;
        private BluetoothGatt? _gatt;
        private TextView? _displayTextView;
        private LineChart? _lineChart;
        private LineDataSet? _dataSet;
        private List<Entry> _entries = new List<Entry>();
        private float _cumulativeValue = 0;
        private Timer? _timer;
        private BluetoothGattCharacteristic? _writeCharacteristic;
        private BluetoothGattCharacteristic? _notifyCharacteristic;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.bluetooth_communication);

            // 获取传递的参数
            var deviceAddress = Intent.GetStringExtra("deviceAddress");
            var serviceUUIDStr = Intent.GetStringExtra("serviceUUID");
            var writeUUIDStr = Intent.GetStringExtra("writeUUID");
            var notifyUUIDStr = Intent.GetStringExtra("notifyUUID");

            var serviceUUID = UUID.FromString(serviceUUIDStr);
            var writeUUID = UUID.FromString(writeUUIDStr);
            var notifyUUID = UUID.FromString(notifyUUIDStr);

            // 初始化蓝牙连接
            _device = BluetoothAdapter.DefaultAdapter.GetRemoteDevice(deviceAddress);
            _gatt = _device.ConnectGatt(this, false, new MyGattCallback(this, serviceUUID, writeUUID, notifyUUID));

            // 初始化UI
            _displayTextView = FindViewById<TextView>(Resource.Id.tvDisplay);
            _lineChart = FindViewById<LineChart>(Resource.Id.lineChart);

            // 初始化图表
            InitChart();

            // 启动定时器，每秒更新一次图表和累计值
            _timer = new Timer(1000);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }

        private void InitChart()
        {
            _dataSet = new LineDataSet(_entries, "辐射剂量率")
            {
                Color = Android.Graphics.Color.Green,
                LineWidth = 2f,
                CircleRadius = 3f
            };

            _dataSet.SetDrawValues(false); // Use SetDrawValues method instead

            LineData lineData = new LineData(_dataSet);
            _lineChart.Data = lineData;

            XAxis xAxis = _lineChart.XAxis;
            xAxis.Position = XAxis.XAxisPosition.Bottom;

            YAxis leftAxis = _lineChart.AxisLeft;
            leftAxis.AxisMinimum = 0f;

            YAxis rightAxis = _lineChart.AxisRight;
            rightAxis.Enabled = false;

            Legend legend = _lineChart.Legend;
            legend.Enabled = true;
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            RunOnUiThread(() =>
            {
                // 更新图表
                _dataSet.NotifyDataSetChanged();
                _lineChart.Data.NotifyDataChanged();
                _lineChart.NotifyDataSetChanged();
                _lineChart.Invalidate();

                // 更新累计值
                float cumulativeValue = _entries.Sum(entry => entry.GetY()) / 3600;
                _displayTextView.Text = $"累计值: {cumulativeValue:F3} (μSv)\n辐射剂量率: {_entries.LastOrDefault()?.GetY() ?? 0:F3} (μSv/h)";
            });
        }

        // 处理特征值变化（接收数据）
        private void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            if (characteristic == null || _notifyCharacteristic == null) return;

            if (characteristic.Uuid == _notifyCharacteristic.Uuid)
            {
                var value = characteristic.GetValue();
                if (value != null)
                {
                    var str = Encoding.UTF8.GetString(value);
                    if (float.TryParse(str, out float radiationRate))
                    {
                        _entries.Add(new Entry(_entries.Count, radiationRate));
                    }
                }
            }
        }

        // 更新显示框
        private void UpdateDisplay(string text, Android.Graphics.Color color)
        {
            RunOnUiThread(() =>
            {
                _displayTextView.Text += text;
                _displayTextView.SetTextColor(color);
            });
        }

        // 释放资源
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_gatt != null)
            {
                _gatt.Disconnect();
                _gatt.Close();
                _gatt.Dispose();
                _gatt = null;
            }
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
        }

        // 自定义的BluetoothGattCallback类
        private class MyGattCallback : BluetoothGattCallback
        {
            private BluetoothCommunicationActivity _activity;
            private readonly UUID _serviceUUID;
            private readonly UUID _writeUUID;
            private readonly UUID _notifyUUID;

            public MyGattCallback(BluetoothCommunicationActivity activity, UUID serviceUUID, UUID writeUUID, UUID notifyUUID)
            {
                _activity = activity;
                _serviceUUID = serviceUUID;
                _writeUUID = writeUUID;
                _notifyUUID = notifyUUID;
            }

            public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
            {
                base.OnConnectionStateChange(gatt, status, newState);
                if (newState == ProfileState.Connected)
                {
                    _activity.UpdateDisplay("连接成功，正在发现服务...\n", Android.Graphics.Color.Green);
                    gatt.DiscoverServices();
                }
                else if (newState == ProfileState.Disconnected)
                {
                    _activity.UpdateDisplay("连接断开\n", Android.Graphics.Color.Red);
                }
            }

            public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
            {
                base.OnServicesDiscovered(gatt, status);
                if (status == GattStatus.Success)
                {
                    // 获取服务和特征
                    var service = gatt.GetService(_serviceUUID);
                    if (service == null)
                    {
                        _activity.UpdateDisplay("服务未找到，请检查UUID\n", Android.Graphics.Color.Red);
                        return;
                    }

                    _activity._writeCharacteristic = service.GetCharacteristic(_writeUUID);
                    _activity._notifyCharacteristic = service.GetCharacteristic(_notifyUUID);

                    if (_activity._writeCharacteristic == null || _activity._notifyCharacteristic == null)
                    {
                        _activity.UpdateDisplay("特征未找到，请检查UUID\n", Android.Graphics.Color.Red);
                        return;
                    }

                    // 启动通知
                    gatt.SetCharacteristicNotification(_activity._notifyCharacteristic, true);
                    var descriptor = _activity._notifyCharacteristic.GetDescriptor(UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"));
                    if (descriptor != null)
                    {
                        descriptor.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
                        gatt.WriteDescriptor(descriptor);
                    }

                    _activity.UpdateDisplay("服务和特征就绪，可以开始通信\n", Android.Graphics.Color.Green);
                }
                else
                {
                    _activity.UpdateDisplay("服务发现失败，请重试\n", Android.Graphics.Color.Red);
                }
            }

            public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
            {
                base.OnCharacteristicChanged(gatt, characteristic);
                _activity.OnCharacteristicChanged(gatt, characteristic);
            }
        }
    }
}

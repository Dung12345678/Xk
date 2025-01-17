using System;
using System.Threading;
using Vila.Threading.Queue;

using InControls.Common;

namespace InControls.PLC.FX
{
	public sealed class FxCommController
	{

		#region 读取实时数据的间竵E周期（单位：脕E�
		private readonly int READ_REAL_DATA_INTERNAL = 5;
		#endregion

		#region 通讯控制器静态实例 Instance 及属性实现代聛E		
        private static FxCommController _Instance;

		public static FxCommController Instance
		{
			get
			{
				if (_Instance == null) {
					lock (typeof(FxCommController)) {
						_Instance = new FxCommController(true);
					}
				}
				return FxCommController._Instance;
			}
		}
		#endregion


		private FxSerialDeamon _SerialDeamon;

		private DataBlockQueue<FxCommandArgs> _DataBlockQueue;

		private System.Threading.Timer _Timer;
		private Thread _Thead;
		private bool _StopTheadFlag;							// 用于停止线程的眮E�

		private DateTime _FirstFailtTime;						// 首次通讯失败时刻

		private EventWaitHandle _WaitHandle;                     // 临时同步对象。如果是同步调用，则使用本等待对蟻E
		/// <summary>
		/// 从默认串口启动本控制苼E		/// 例如从 COM1 自动启动
		/// </summary>
		/// <param name="autoStartFromDefaultSerialPort"></param>
		private FxCommController(bool autoStartFromDefaultSerialPort)
		{
			_WaitHandle = null;

			_DataBlockQueue = new DataBlockQueue<FxCommandArgs>(64, true);

			_SerialDeamon = null;
			_Thead = null;
			_Timer = null;
			_FirstFailtTime = DateTime.MaxValue;
			_StopTheadFlag = false;

			if (autoStartFromDefaultSerialPort) Start(1);
		}

		/// <summary>
		/// 启动觼ES232的通讯线程
		/// </summary>
		public void Start(int portNo)
		{
			_SerialDeamon = new FxSerialDeamon();
			_SerialDeamon.Start(portNo);

			_Thead = new Thread(new ThreadStart(this.DealwithCommunicationRoute));
			_Thead.Name = "FxCommController";
			_Thead.IsBackground = true;
			_Thead.Start();

			_Timer = new Timer(new TimerCallback(TimerRoute));
			_Timer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromDays(1));
		}

		/// <summary>
		/// 关闭线程
		/// </summary>
		public void Stop()
		{
			_SerialDeamon.Dispose();
			_SerialDeamon = null;

			_Timer.Dispose();
			_Timer = null;

			_Thead.Abort();
			_Thead.Join();
			_Thead = null;
		}

		/// <summary>
		/// 襾E椒⑺兔丒⒘⒖谭祷�
		/// 函数内部实现发送队列、以及超时机制
		/// </summary>
		/// <param name="response">脕E�</param>
		/// <param name="cmdData">脕E谌荩� 0xAA,0xCC,0x83 开头的byte[] </param>
		public void PostCall(short cmd, byte[] cmdData)
		{
			FxCommandArgs cmdArgs = new FxCommandArgs(cmd, cmdData);
			_DataBlockQueue.Add(cmdArgs);
		}

		/// <summary>
		/// 襾E椒⑺兔丒丒		/// 用于发送后就不管的“襾E矫丒�
		/// </summary>
		/// <param name="cmdArgs">参数的OnSendHandle成员应该为空白</param>
		public void PostCall(FxCommandArgs cmdArgs)
		{
			_DataBlockQueue.Add(cmdArgs);
		}

		/// <summary>
		/// 襾E椒⑺兔丒丒		/// 发送者应该实现了 IDataPackageEvent，且需提供引用
		/// </summary>
		/// <param name="cmdArgs">参数的OnSendHandle成员应该为空白</param>
		/// <param name="onSendHandle">根据需要，用户可提供回调祦E/param>
		//public void PostCall(CommandEventArgs cmdArgs, IDataPackageEvent onSendHandle)
		//{
		//    System.Diagnostics.Debug.Assert(onSendHandle != null, "调用者必衼E迪諭DataPackageEvent，且将引用传葋E竞�");

		//    if (cmdArgs.OnSendHandle == null) cmdArgs.OnSendHandle = onSendHandle;
		//    _DataBlockQueue.Add(cmdArgs);
		//}

		/// <summary>
		/// 同步发送脕E丒		/// </summary>
		/// <param name="cmdArgs">参数的OnSendHandle成员必衼E持滴饔谜咭�</param>
		public FxCommandResponse SendCall(FxCommandArgs cmdArgs, TimeSpan timeout)
		{
			if (cmdArgs == null) return (null);

			if (_WaitHandle == null) {
				_WaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
			}

			_DataBlockQueue.Add(cmdArgs);

			_WaitHandle.WaitOne(timeout, true);
			_WaitHandle.Close();
			_WaitHandle = null;

			return (cmdArgs.Result);
		}

		/// <summary>
		/// 同步发送脕E⒌却祷�
		/// 函数内部实现发送队列、以及超时机制
		/// </summary>
		/// <param name="response">脕E�</param>
		/// <param name="channelNo">通道号，默认0</param>
		/// <param name="cmdData">脕E谌荩� 0xAA,0xCC,0x83 开头的byte[] </param>
		/// <param name="timeout">畜超时设定</param>
		public FxCommandResponse SendCall(short cmd, byte channelNo, byte[] cmdData, TimeSpan timeout)
		{
			if (cmdData == null) {              // 如果脕E挥泄乖丒蚰瞎乖丒薏问拿丒丒				FxCommandResponse prm = new FxCommandResponse(ResultCodeConst.rcNotSettting, cmdData);
				//cmdData = _CommanderHelper.MakeSmart(response, targetPort, prm);
			}

			FxCommandArgs arg = new FxCommandArgs(cmd, cmdData, channelNo);

			return (SendCall(arg, timeout));
		}


		/// <summary>
		/// 周期性发送读实时数据脕E丒		/// 策略：
		///     1、仅仅在空闲时产蓙E		///     2、如果发送队列中有内容，则不再产蓙E		/// </summary>
		private void TimerRoute(Object state)
		{
			if (_DataBlockQueue.QueueLength == 0) {
				// 如果待发送队列中没有内容，才允衼E1:读取实时数据
				byte[] cmdbuff = new byte[11];
				_DataBlockQueue.Add(new FxCommandArgs(0, cmdbuff, 0));

				// 如果待发送队列中没有内容，才允衼E2:用联机脕E袢∈笨獭⒆刺畔�
				_DataBlockQueue.Add(new FxCommandArgs(0, cmdbuff, 0));
			}
			_Timer.Change(TimeSpan.FromSeconds(READ_REAL_DATA_INTERNAL), TimeSpan.FromDays(1));
		}

		/// <summary>
		/// 内部用于处历嬲发的独立线程
		/// 用于内部实现同步收发
		/// </summary>
		private void DealwithCommunicationRoute()
		{
			while (!_StopTheadFlag) {
				if (_DataBlockQueue.QueueLength == 0) {
					Thread.Sleep(20);
					continue;
				}

				FxCommandArgs arg = _DataBlockQueue.Pick();
				if (arg == null) continue;

				FxCommandResponse result = _SerialDeamon.Send(arg.ChannelNo, arg.Data, arg.Data.Length);

				if (result.ResultCode == ResultCodeConst.rcFailt) {
					// 如果返回失败，则累计总失败时间，到磥E敝岛螅枰赨I界面上有反馈
					if (DateTime.Now.Subtract(_FirstFailtTime).TotalSeconds > 30) {
						_FirstFailtTime = DateTime.Now;
					} else if (_FirstFailtTime == DateTime.MaxValue) {
						_FirstFailtTime = DateTime.Now;
					}

				} else if (result != null) {
					_FirstFailtTime = DateTime.MaxValue;

					switch (arg.Cmd) {
					case 1:                             // 如果是...
						break;
					default:
						break;
					}
				} else {
				}

				if (_WaitHandle != null) {
					arg.Result = result;
					_WaitHandle.Set();
				}

				//if (arg.OnSendHandle != null) {
				//    arg.OnSendHandle.OnPackageRecived(result);
				//}

			}
			System.Diagnostics.Debug.Assert(false, "退出线程了！！");
			_Thead = null;
		}
	}
}

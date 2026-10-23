using System;
using System.Collections.Generic;
using System.Text;
using MealCardSystem.Util;
using ReaderB;

namespace MealCardSystem.Reader
{
    /// <summary>
    /// KL9007U 读卡器支持的波特率。数值是 DLL 内部编码，<b>不是</b> bps 数值本身。
    ///
    /// 编码由基本 DLL 的跳转表决定：0/1/2/4/5/6 → 9600/19200/38400/56000/57600/115200，
    /// 而 <b>3 是非法的</b>（传入会直接返回 0x30 通讯错误且不打开端口），因此枚举里特意跳过 3。
    /// </summary>
    public enum ReaderBaud : byte
    {
        Bps9600 = 0,
        Bps19200 = 1,
        Bps38400 = 2,
        Bps56000 = 4,
        Bps57600 = 5,
        Bps115200 = 6
    }

    /// <summary>读卡器返回码的中文说明（对应开发包示例的 GetReturnCodeDesc）。</summary>
    public static class ReaderCodes
    {
        /// <summary>模块级返回码说明。</summary>
        public static string Describe(int code)
        {
            switch (code)
            {
                case 0x00: return "操作成功";
                case 0x01: return "询查时间结束前返回";
                case 0x02: return "指定的询查时间溢出";
                case 0x03: return "本条消息之后，还有消息";
                case 0x04: return "读写模块存储空间已满";
                case 0x05: return "访问密码错误";
                case 0x09: return "销毁密码错误";
                case 0x0A: return "销毁密码不能为全0";
                case 0x0B: return "电子标签不支持该命令";
                case 0x0C: return "对该命令，访问密码不能为全0";
                case 0x0D: return "电子标签已经被设置了读保护，不能再次设置";
                case 0x0E: return "电子标签没有被设置读保护，不需要解锁";
                case 0x10: return "有字节空间被锁定，写入失败";
                case 0x11: return "不能锁定";
                case 0x12: return "已经锁定，不能再次锁定";
                case 0x13: return "参数保存失败，但设置的值在读写模块断电前有效";
                case 0x14: return "无法调整";
                case 0x15: return "询查时间结束前返回";
                case 0x16: return "指定的询查时间溢出";
                case 0x17: return "本条消息之后，还有消息";
                case 0x18: return "读写模块存储空间已满";
                case 0x19: return "电子标签不支持该命令或者访问密码不能为0";
                case 0x30: return "通讯错误";
                case 0x31: return "CRC 校验错误";
                case 0x32: return "返回数据长度有错误";
                case 0x33: return "通讯繁忙，设备正在执行其他指令";
                case 0x34: return "繁忙，指令正在执行";
                case 0x35: return "端口已打开";
                case 0x36: return "端口已关闭";
                case 0x37: return "无效句柄";
                case 0x38: return "无效端口";
                case 0xEE: return "返回指令错误";
                case 0xFA: return "有电子标签，但通信不畅，无法操作";
                case 0xFB: return "无电子标签可操作";
                case 0xFC: return "电子标签返回错误代码";
                case 0xFD: return "命令长度错误";
                case 0xFE: return "不合法的命令";
                case 0xFF: return "参数错误";
                default: return "未知返回码 0x" + code.ToString("X2");
            }
        }

        /// <summary>标签级错误码说明（read/write 的 errorcode 输出参数）。</summary>
        public static string DescribeTagError(int code)
        {
            switch (code)
            {
                case 0x00: return "其它错误";
                case 0x03: return "存储器超限或不被支持的 PC 值";
                case 0x04: return "存储器锁定";
                case 0x0B: return "电源不足";
                case 0x0F: return "非特定错误";
                default: return "标签错误码 0x" + code.ToString("X2");
            }
        }

        /// <summary>把连接失败返回码翻译成给操作员看的可执行建议。</summary>
        public static string DescribeConnectFailure(int code)
        {
            switch (code)
            {
                case 0x30:
                    return "通讯错误：读卡器未响应。请确认读卡器已通电、USB 线已插好、串口未被官方 Demo 等其它程序占用。";
                case 0x35:
                    return "串口已被打开：请先关闭其它占用该串口的程序。";
                case 0x38:
                    return "无效端口：该 COM 口不存在。";
                case 0x37:
                    return "无效句柄。";
                case 0xFB:
                    return "未找到读卡器。";
                default:
                    return "连接失败：" + Describe(code);
            }
        }
    }

    /// <summary>读卡器连接状态快照，供界面显示。</summary>
    public class ReaderStatus
    {
        public bool Connected { get; set; }
        public int PortNumber { get; set; }
        public string PortName { get; set; }
        public string Model { get; set; }
        public string Version { get; set; }
        public int PowerDbm { get; set; }

        public string Summary
        {
            get
            {
                if (!Connected)
                {
                    return "读卡器未连接";
                }
                return "已连接 " + PortName + " · 型号 " + Model + " · 固件 " + Version + " · 功率 " + PowerDbm + "dBm";
            }
        }
    }

    /// <summary>
    /// KL9007U 读卡器的封装。对上层隐藏 StaticClassReaderB 的调用细节。
    ///
    /// 刷卡语义：本类持续调用 <see cref="Poll"/> 做轮询，但只在卡片"新出现"时上报一次，
    /// 卡片一直放在读卡器上不会重复触发 —— 这对消费功能是必须的，否则会连续扣款。
    /// </summary>
    public class RfidReader : IDisposable
    {
        /// <summary>读写器广播地址。开发包默认值，单机单读卡器场景无需修改。</summary>
        private const byte DefaultComAddr = 0xFF;

        /// <summary>盘点缓冲区大小。EPC 最长 62 字节，5000 字节足够容纳多卡一轮的结果。</summary>
        private const int InventoryBufferSize = 5000;

        private readonly object _sync = new object();
        private byte _comAddr = DefaultComAddr;
        private int _handle = -1;
        private bool _connected;
        private bool _disposed;

        // 刷卡去重：记录每张卡最后一次被看到的时间，以及当前是否已"锁定上报"
        private readonly Dictionary<string, DateTime> _lastSeen = new Dictionary<string, DateTime>();
        private readonly HashSet<string> _latched = new HashSet<string>();

        public RfidReader()
        {
            // 读卡器偶尔会漏读一两个轮询周期，宽限 2 秒可避免卡片没动却被判定为"重新刷卡"
            RemoveGraceSeconds = 2.0;
            Status = new ReaderStatus();
        }

        /// <summary>卡片离开天线场多久后视为"已拿走"（秒）。</summary>
        public double RemoveGraceSeconds { get; set; }

        public bool IsConnected
        {
            get { return _connected; }
        }

        public ReaderStatus Status { get; private set; }

        // ---------- 连接 ----------

        /// <summary>
        /// 自动探测并连接读卡器。会依次尝试各波特率，直到读卡器响应。
        /// </summary>
        /// <param name="preferredBaud">优先尝试的波特率（默认 57600，与开发包示例截图一致）。</param>
        public bool ConnectAuto(ReaderBaud preferredBaud, out string error)
        {
            lock (_sync)
            {
                if (_connected)
                {
                    error = null;
                    return true;
                }

                // 先试用户指定的波特率，再按常见程度依次回退（注意跳过非法的 3）
                ReaderBaud[] order = new ReaderBaud[]
                {
                    preferredBaud,
                    ReaderBaud.Bps57600,
                    ReaderBaud.Bps115200,
                    ReaderBaud.Bps38400,
                    ReaderBaud.Bps19200,
                    ReaderBaud.Bps9600,
                    ReaderBaud.Bps56000
                };

                List<ReaderBaud> tried = new List<ReaderBaud>();
                int lastCode = 0;

                for (int i = 0; i < order.Length; i++)
                {
                    if (tried.Contains(order[i]))
                    {
                        continue;
                    }
                    tried.Add(order[i]);

                    int port = 0;
                    byte addr = DefaultComAddr;
                    int handle = -1;
                    int rc = StaticClassReaderB.AutoOpenComPort(ref port, ref addr, (byte)order[i], ref handle);

                    if (rc == 0)
                    {
                        _comAddr = addr;
                        _handle = handle;
                        _connected = true;
                        ReadReaderInfo();
                        error = null;
                        return true;
                    }

                    // 探测失败时若已经打开了句柄，先关掉再继续试下一个波特率，避免占用串口
                    if (handle >= 0)
                    {
                        try { StaticClassReaderB.CloseSpecComPort(handle); }
                        catch (Exception) { }
                    }

                    lastCode = rc;
                }

                error = ReaderCodes.DescribeConnectFailure(lastCode);
                return false;
            }
        }

        /// <summary>连接到指定的 COM 口（不自动探测端口，但仍会尝试各波特率）。</summary>
        public bool Connect(int comPort, ReaderBaud preferredBaud, out string error)
        {
            lock (_sync)
            {
                if (_connected)
                {
                    error = null;
                    return true;
                }

                ReaderBaud[] order = new ReaderBaud[]
                {
                    preferredBaud,
                    ReaderBaud.Bps57600,
                    ReaderBaud.Bps115200,
                    ReaderBaud.Bps38400,
                    ReaderBaud.Bps19200,
                    ReaderBaud.Bps9600,
                    ReaderBaud.Bps56000
                };

                List<ReaderBaud> tried = new List<ReaderBaud>();
                int lastCode = 0;

                for (int i = 0; i < order.Length; i++)
                {
                    if (tried.Contains(order[i]))
                    {
                        continue;
                    }
                    tried.Add(order[i]);

                    byte addr = DefaultComAddr;
                    int handle = -1;
                    int rc = StaticClassReaderB.OpenComPort(comPort, ref addr, (byte)order[i], ref handle);

                    if (rc == 0)
                    {
                        _comAddr = addr;
                        _handle = handle;
                        _connected = true;
                        ReadReaderInfo();
                        error = null;
                        return true;
                    }

                    if (handle >= 0)
                    {
                        try { StaticClassReaderB.CloseSpecComPort(handle); }
                        catch (Exception) { }
                    }

                    // 端口已被占用时无需再换波特率重试
                    if (rc == 0x35)
                    {
                        lastCode = rc;
                        break;
                    }

                    lastCode = rc;
                }

                error = ReaderCodes.DescribeConnectFailure(lastCode);
                return false;
            }
        }

        /// <summary>读取型号/固件版本，作为"确实连上了"的证据显示在界面上。</summary>
        private void ReadReaderInfo()
        {
            Status.Connected = true;
            Status.PortNumber = _handle;
            Status.PortName = "COM" + _handle;
            Status.Model = "UHFReader18";
            Status.Version = "未知";
            Status.PowerDbm = 0;

            try
            {
                byte addr = _comAddr;
                byte[] versionInfo = new byte[2];
                byte readerType = 0;
                byte[] trType = new byte[2];
                byte dmaxfre = 0, dminfre = 0, powerdBm = 0, scanTime = 0;

                int rc = StaticClassReaderB.GetReaderInformation(
                    ref addr, versionInfo, ref readerType, trType,
                    ref dmaxfre, ref dminfre, ref powerdBm, ref scanTime, _handle);

                if (rc == 0)
                {
                    Status.Version = versionInfo[0].ToString("00") + "." + versionInfo[1].ToString("00");
                    Status.PowerDbm = powerdBm;
                }
                else
                {
                    // 拿不到版本信息不影响使用，仅作提示
                    Status.Version = "未读取(" + ReaderCodes.Describe(rc) + ")";
                }
            }
            catch (Exception)
            {
                Status.Version = "未读取";
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (_handle >= 0)
                {
                    try { StaticClassReaderB.CloseSpecComPort(_handle); }
                    catch (Exception) { }
                }
                _handle = -1;
                _connected = false;
                _latched.Clear();
                _lastSeen.Clear();
                Status = new ReaderStatus();
            }
        }

        /// <summary>清空"已上报"标记，让当前在场的卡片可以重新被上报一次。</summary>
        public void ResetSwipeLatch()
        {
            lock (_sync)
            {
                _latched.Clear();
            }
        }

        // ---------- 盘点 ----------

        /// <summary>
        /// 轮询一次，返回<b>本次新出现</b>的卡片 EPC 列表（已规整为大写十六进制）。
        ///
        /// 同一张卡只要不拿走就不会重复返回；拿走后需超过 <see cref="RemoveGraceSeconds"/> 秒
        /// 才会被判定为离开，之后再放上来会重新返回。
        /// </summary>
        public List<string> Poll()
        {
            List<string> found = InventoryRaw();
            List<string> appeared = new List<string>();
            DateTime now = DateTime.Now;

            lock (_sync)
            {
                // 1) 记录本轮的在场卡，并挑出首次出现的
                for (int i = 0; i < found.Count; i++)
                {
                    string epc = found[i];
                    _lastSeen[epc] = now;
                    if (!_latched.Contains(epc))
                    {
                        _latched.Add(epc);
                        appeared.Add(epc);
                    }
                }

                // 2) 把超时未再看到的卡从"已上报"集合里摘掉，使其下次可再次触发
                List<string> gone = new List<string>();
                foreach (KeyValuePair<string, DateTime> kv in _lastSeen)
                {
                    if ((now - kv.Value).TotalSeconds > RemoveGraceSeconds)
                    {
                        gone.Add(kv.Key);
                    }
                }
                for (int i = 0; i < gone.Count; i++)
                {
                    _lastSeen.Remove(gone[i]);
                    _latched.Remove(gone[i]);
                }
            }

            return appeared;
        }

        /// <summary>
        /// 单次盘点，返回当前天线场内所有卡片的 EPC（不做去重）。
        /// 出错或无卡时返回空列表，不抛异常，以便轮询定时器安全调用。
        /// </summary>
        public List<string> InventoryRaw()
        {
            List<string> result = new List<string>();

            if (!_connected)
            {
                return result;
            }

            lock (_sync)
            {
                try
                {
                    byte addr = _comAddr;
                    byte[] buffer = new byte[InventoryBufferSize];
                    int totalLen = 0;
                    int cardNum = 0;

                    // AdrTID/LenTID/TIDFlag 全 0 表示返回 EPC（本系统以 EPC 作为卡号）
                    int rc = StaticClassReaderB.Inventory_G2(
                        ref addr, 0, 0, 0, buffer, ref totalLen, ref cardNum, _handle);

                    // 1/2/3/4 = 本轮询查正常结束，缓冲区有效；0 = 操作成功；0xFB = 无卡
                    bool ok = (rc == 1 || rc == 2 || rc == 3 || rc == 4 || rc == 0);
                    if (!ok || cardNum <= 0)
                    {
                        return result;
                    }

                    // 缓冲区格式：[长度字节][EPC数据][长度字节][EPC数据]...
                    if (totalLen > buffer.Length)
                    {
                        totalLen = buffer.Length;
                    }

                    int m = 0;
                    for (int i = 0; i < cardNum; i++)
                    {
                        // 越界保护：设备返回的数据若被截断，宁可少收一张卡也不要抛异常
                        if (m >= totalLen)
                        {
                            break;
                        }

                        int epcLen = buffer[m];
                        if (epcLen <= 0 || m + 1 + epcLen > totalLen)
                        {
                            break;
                        }

                        byte[] epcBytes = new byte[epcLen];
                        Array.Copy(buffer, m + 1, epcBytes, 0, epcLen);
                        result.Add(Hex.ToHexString(epcBytes));

                        m += epcLen + 1;
                    }
                }
                catch (Exception)
                {
                    // 串口抖动时不中断轮询，下一轮自然恢复
                }
            }

            return result;
        }

        // ---------- 读写数据块（当前业务不写卡，保留给后续扩展）----------

        /// <summary>读取标签存储区。<paramref name="mem"/> 取值 0=保留区 1=EPC区 2=TID区 3=用户区。</summary>
        public byte[] ReadBlock(string epc, byte mem, byte wordPtr, byte wordCount, out string error)
        {
            error = null;

            if (!_connected)
            {
                error = "读卡器未连接。";
                return null;
            }

            // 开发包示例对所有存储区统一限制：起始地址 + 字数 不得超过 120
            if (wordPtr + wordCount > 120)
            {
                error = "起始地址加长度超过 120 字，超出允许范围。";
                return null;
            }

            lock (_sync)
            {
                try
                {
                    byte addr = _comAddr;
                    byte[] targetEpc = Hex.FromHexString(Hex.NormalizeEpc(epc));
                    byte[] password = new byte[] { 0, 0, 0, 0 };
                    byte[] data = new byte[wordCount * 2];
                    int errorCode = -1;

                    int rc = StaticClassReaderB.ReadCard_G2(
                        ref addr, targetEpc, mem, wordPtr, wordCount, password,
                        0, 0, 0, data, (byte)targetEpc.Length, ref errorCode, _handle);

                    if (rc != 0)
                    {
                        error = ReaderCodes.Describe(rc);
                        return null;
                    }
                    if (errorCode >= 0 && errorCode != 0)
                    {
                        error = ReaderCodes.DescribeTagError(errorCode);
                        return null;
                    }

                    return data;
                }
                catch (Exception ex)
                {
                    error = "读取标签失败：" + ex.Message;
                    return null;
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Disconnect();
            }
        }
    }
}

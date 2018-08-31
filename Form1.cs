using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;


namespace fork_ffmpeg
{
    public partial class Form1 : Form
    {
        Process _process;
        const int frameWidth = 320;
        const int frameHeight = 240;
        char[] _pipeErr = new char[1024];
        byte[] _pipeOut = new byte[8192];
        byte[] _frame = new byte[frameWidth * frameHeight * 3];
        const int dropSeqFirst = 274;
        const int dropSeqPerFrame = 32;
        int _dropSeq = dropSeqFirst;
        int _frameDataIndex = 0;
        int _frames = 0;

        public Form1()
        {
            InitializeComponent();

            ProcessStartInfo si = new ProcessStartInfo("C:/bin/ffmpeg.exe", $"-f lavfi -i testsrc=duration=5:size={frameWidth}x{frameHeight}:rate=1 -c:v rawvideo -f nut -")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            _process = Process.Start(si);
            _process.SynchronizingObject = this;
            _process.StandardError.ReadAsync(_pipeErr, 0, 1024).ContinueWith(OnFFmpegError);
            _process.StandardOutput.BaseStream.ReadAsync(_pipeOut, 0, _pipeOut.Length).ContinueWith(OnFFmpegOutput);
            _process.EnableRaisingEvents = true;
            _process.Exited += new EventHandler(OnFFmpegExit);
        }

        void OnFFmpegExit(object sender, EventArgs e)
        {
            Console.Out.WriteLine($"\n---- FFmpeg exit: {_process.ExitCode}");
        }

        void OnFFmpegError(Task<int> result)
        {
            if (result.Result > 0)
            {
                Console.Out.Write(_pipeErr, 0, result.Result);
            }
            if (!_process.HasExited)
            {
                _process.StandardError.ReadAsync(_pipeErr, 0, 1024).ContinueWith(OnFFmpegError);
            }
        }

        void OnFFmpegOutput(Task<int> result)
        {
            int readBytes = result.Result;
            if (readBytes > 0)
            {
                Action<int> dropBytes = (len) =>
                {
                    int r = result.Result - len;
                    if (r > 0)
                    {
                        byte[] tmp = new byte[r];
                        Buffer.BlockCopy(_pipeOut, len, tmp, 0, r);
                        tmp.CopyTo(_pipeOut, 0);
                    }
                    _dropSeq -= len;
                    readBytes -= len;
                };
                if (_dropSeq > 0)
                {
                    if (_dropSeq <= readBytes)
                    {
                        dropBytes(_dropSeq);
                    } else
                    {
                        dropBytes(readBytes);
                    }
                }
            }
            if (readBytes > 0)
            {
                byte[] restBytes = null;
                int r = _frame.Length - _frameDataIndex;
                if (r >= readBytes)
                {
                    Buffer.BlockCopy(_pipeOut, 0, _frame, _frameDataIndex, readBytes);
                    _frameDataIndex += readBytes;
                }
                else
                {
                    int bytes = _frame.Length - _frameDataIndex;
                    Buffer.BlockCopy(_pipeOut, 0, _frame, _frameDataIndex, bytes);
                    _frameDataIndex += bytes;
                    restBytes = new byte[readBytes - bytes];
                    Buffer.BlockCopy(_pipeOut, bytes, restBytes, 0, restBytes.Length);
                }
                Console.Out.WriteLine($"[{_frames}] {readBytes} bytes of {_frameDataIndex}/{_frame.Length} incoming.\n");
                if (_frameDataIndex == _frame.Length)
                {
                    _frames++;
                    _frameDataIndex = 0;
                    _dropSeq = dropSeqPerFrame;
                    Console.Out.WriteLine($"==> SHOW {_frames} frame.\n");
                    unsafe
                    {
                        fixed (byte* ptr = _frame)
                        {
                            using (Bitmap bmp = new Bitmap(frameWidth, frameHeight, frameWidth * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(ptr)))
                            {
                                bmp.Save($"C:/Users/terasita/Downloads/frame{_frames}.png");
                            }
                        }
                    }
                    if (restBytes != null)
                    {
                        restBytes.CopyTo(_frame, 0);
                        _frameDataIndex = restBytes.Length;
                    }
                }
                Console.Out.WriteLine($"[{_frames}] {_frameDataIndex}/{_frame.Length} rest.\n");
            }
            if (!_process.HasExited)
            {
                _process.StandardOutput.BaseStream.ReadAsync(_pipeOut, 0, _pipeOut.Length).ContinueWith(OnFFmpegOutput);
            }
        }
    }
}

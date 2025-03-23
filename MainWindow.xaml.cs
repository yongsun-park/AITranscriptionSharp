using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MessageBox = System.Windows.MessageBox;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;

namespace AITranscriptionSharp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Win32 API 함수 선언
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000;
    private const uint MOD_WIN = 0x0008;
    private const uint VK_A = 0x41; // 'A' 키
    private const uint VK_OEM_PLUS = 0xBB; // '+' 키
    private const uint MOD_CONTROL_SHIFT = 0x0002 | 0x0004;

    private HwndSource? _source;

    public MainWindow()
    {
        InitializeComponent();
    }

    // Window가 생성되고 Source가 초기화될 때 핫키 등록
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(HwndHook);

        // 전역 단축키 등록: Ctrl + Shift + A
        if (!RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL_SHIFT, VK_A))
        {
            int errorCode = Marshal.GetLastWin32Error();
            MessageBox.Show($"전역 단축키 등록에 실패했습니다. 오류 코드: {errorCode}");
        }
    }

    // 핫키 메시지 처리
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // 단축키가 눌렸을 때 처리할 내용
            OnHotKeyPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // 단축키가 눌렸을 때 호출되는 함수
    private async void OnHotKeyPressed()
    {
        string transcriptionResult = await PerformTranscriptionAsync();
        if (!string.IsNullOrEmpty(transcriptionResult))
        {
            SendKeys.SendWait(transcriptionResult);
        }
    }

    private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        string transcriptionResult = await PerformTranscriptionAsync();
        if (!string.IsNullOrEmpty(transcriptionResult))
        {
            SendKeys.SendWait(transcriptionResult);
        }
    }

    private async Task<string> PerformTranscriptionAsync()
    {
        // Replace with actual audio recording logic
        byte[] audioData = await RecordAudioAsync();

        // Replace with your OpenAI API key
        string apiKey = "YOUR_OPENAI";
        string apiUrl = "https://api.openai.com/v1/audio/transcriptions";

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using MultipartFormDataContent content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(audioData), "file", "audio.wav"); // 변경: m4a 확장자 사용
        // content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent("gpt-4o-mini-transcribe"), "model");
        content.Add(new StringContent("text"), "response_format");

        HttpResponseMessage response = await client.PostAsync(apiUrl, content);
        if (response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync();
            // Parse the transcription result from the response (adjust parsing as needed)
            return responseBody; // Simplified for demonstration
        }
        else
        {
            MessageBox.Show($"Transcription failed: {response.StatusCode}");
            return string.Empty;
        }
    }

    private async Task<byte[]> RecordAudioAsync()
    {
        using var waveIn = new WaveInEvent();
        waveIn.WaveFormat = new WaveFormat(44100, 1); // 44100 Hz, mono
        using var memoryStream = new MemoryStream();
        using var writer = new WaveFileWriter(memoryStream, waveIn.WaveFormat);

        DateTime lastSoundTime = DateTime.Now;
        const short silenceThreshold = 500; // 임계값 (필요 시 조정)

        waveIn.DataAvailable += (s, e) =>
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                if (Math.Abs(sample) > silenceThreshold)
                {
                    lastSoundTime = DateTime.Now;
                    break;
                }
            }
            if (DateTime.Now - lastSoundTime > TimeSpan.FromSeconds(3))
            {
                waveIn.StopRecording();
            }
        };

        var tcs = new TaskCompletionSource<bool>();
        waveIn.RecordingStopped += (s, e) => tcs.SetResult(true);

        waveIn.StartRecording();
        await tcs.Task;
        writer.Flush();
        return memoryStream.ToArray();
    }

    // Window 종료 시 핫키 해제
    protected override void OnClosed(EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HOTKEY_ID);
        base.OnClosed(e);
    }
}
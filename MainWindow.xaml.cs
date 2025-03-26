using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NAudio.Wave;
using MessageBox = System.Windows.MessageBox;
using AITranscriptionSharp.Properties;
using AITranscriptionSharp.Helper;  // Settings 네임스페이스 추가

namespace AITranscriptionSharp
{
    public partial class MainWindow : Window
    {
        // Win32 API 함수 선언
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint VK_A = 0x41; // 'A' 키
        private const uint MOD_CONTROL_SHIFT = 0x0002 | 0x0004;

        private HwndSource? _source;

        public MainWindow()
        {
            InitializeComponent();

            // 애플리케이션이 처음 실행되는 새 버전일 경우 이전 설정을 업그레이드
            if (Settings.Default.IsFirstRun)
            {
                Settings.Default.Upgrade();
                Settings.Default.IsFirstRun = false;
                Settings.Default.Save();
            }

            // 암호화된 API Key 불러오기 (있으면 복호화)
            if (!string.IsNullOrEmpty(Settings.Default.EncryptedOpenAIApiKey))
            {
                try
                {
                    string decryptedKey = EncryptionHelper.DecryptString(Settings.Default.EncryptedOpenAIApiKey);
                    ApiKeyTextBox.Text = decryptedKey;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"API Key 복호화에 실패했습니다: {ex.Message}");
                }
            }

            // 저장된 모델 불러오기
            string savedModel = Settings.Default.SelectedAiModel;
            if (!string.IsNullOrEmpty(savedModel))
            {
                foreach (ComboBoxItem item in AiModelComboBox.Items)
                {
                    if (item.Content.ToString() == savedModel)
                    {
                        AiModelComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // 윈도우 생성 시 핫키 등록
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
                OnHotKeyPressed();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // 단축키가 눌렸을 때 호출되는 함수
        private async void OnHotKeyPressed()
        {
            // 녹음 장치가 없는 경우 예외 처리
            if (WaveInEvent.DeviceCount < 1)
            {
                MessageBox.Show("No audio input device (microphone) detected.");
                return;
            }

            string transcriptionResult = await PerformTranscriptionAsync();
            if (!string.IsNullOrEmpty(transcriptionResult))
            {
                System.Windows.Forms.SendKeys.SendWait(transcriptionResult);
            }
        }

        // Record 버튼 클릭 이벤트 핸들러
        private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
        {
            // 녹음 장치가 없는 경우 예외 처리
            if (WaveInEvent.DeviceCount < 1)
            {
                MessageBox.Show("No audio input device (microphone) detected.");
                return;
            }

            string transcriptionResult = await PerformTranscriptionAsync();
            if (!string.IsNullOrEmpty(transcriptionResult))
            {
                System.Windows.Forms.SendKeys.SendWait(transcriptionResult);
            }
        }

        // API Key 저장 버튼 클릭 이벤트 핸들러 (암호화해서 저장)
        private void SaveApiKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string plainKey = ApiKeyTextBox.Text;
                string encryptedKey = EncryptionHelper.EncryptString(plainKey);
                Settings.Default.EncryptedOpenAIApiKey = encryptedKey;
                Settings.Default.Save();
                MessageBox.Show("API Key가 안전하게 저장되었습니다.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API Key 저장 중 오류 발생: {ex.Message}");
            }
        }

        // ComboBox 선택 변경 시 모델 저장
        private void AiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AiModelComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                Settings.Default.SelectedAiModel = selectedItem.Content.ToString();
                Settings.Default.Save();
            }
        }

        private async Task<string> PerformTranscriptionAsync()
        {
            byte[] audioData = await RecordAudioAsync();

            // 저장된 API Key 사용 (복호화된 값을 사용했으므로 ApiKeyTextBox.Text가 평문임)
            string apiKey = !string.IsNullOrEmpty(ApiKeyTextBox.Text)
                ? ApiKeyTextBox.Text
                : "YOUR_OPENAI_API_KEY";

            string apiUrl = "https://api.openai.com/v1/audio/transcriptions";

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using MultipartFormDataContent content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(audioData), "file", "audio.wav");
            string selectedModel = ((ComboBoxItem)AiModelComboBox.SelectedItem).Content.ToString();
            content.Add(new StringContent(selectedModel), "model");
            content.Add(new StringContent("text"), "response_format");

            HttpResponseMessage response = await client.PostAsync(apiUrl, content);
            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                return responseBody;
            }
            else
            {
                MessageBox.Show($"Transcription failed: {response.StatusCode}");
                return string.Empty;
            }
        }

        private async Task<byte[]> RecordAudioAsync()
        {
            using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(44100, 1) };
            using var memoryStream = new MemoryStream();
            using var writer = new WaveFileWriter(memoryStream, waveIn.WaveFormat);

            DateTime lastSoundTime = DateTime.Now;
            const short silenceThreshold = 500;

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

            try
            {
                waveIn.StartRecording();
                await tcs.Task;
            }
            catch (NAudio.MmException ex)
            {
                MessageBox.Show($"녹음 시작 중 오류가 발생했습니다: {ex.Message}");
                return Array.Empty<byte>();
            }

            writer.Flush();
            return memoryStream.ToArray();
        }
    }
}

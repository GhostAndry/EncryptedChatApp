using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EncryptedChatApp
{
    public partial class ChatForm : Form
    {
        // Networking
        private TcpClient client;
        private TcpListener server;
        private NetworkStream stream;

        // Cryptographic Keys and IVs
        private byte[] aesKey;
        private byte[] aesIV;
        private byte[] hmacKey;

        // RSA Key Pair
        private RSACryptoServiceProvider rsaProvider;

        // Utility
        private const int BufferSize = 4096;

        private void ChatForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Close all sockets and resources
            CloseSockets();
        }

        private void CloseSockets()
        {
            try
            {
                if (stream != null)
                {
                    stream.Close();
                    stream = null;
                }

                if (client != null)
                {
                    client.Close();
                    client = null;
                }

                if (server != null)
                {
                    server.Stop();
                    server = null;
                }

                AppendToChat("Tutti i socket sono stati chiusi correttamente.");
            }
            catch (Exception ex)
            {
                AppendToChat($"Errore durante la chiusura dei socket: {ex.Message}");
            }
        }

        public ChatForm()
        {
            InitializeComponent();

            // Initialize cryptographic keys and IVs
            aesIV = GenerateRandomBytes(16);
            aesKey = GenerateRandomBytes(32);
            hmacKey = GenerateRandomBytes(32);

            // Initialize RSA
            rsaProvider = new RSACryptoServiceProvider(2048);

            // Spostato nel metodo Load per evitare problemi di threading
            this.Load += ChatForm_Load;
            this.FormClosing += ChatForm_FormClosing;
            this.messageTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.MessageTextBox_KeyDown);

        }

        private void ChatForm_Load(object sender, EventArgs e)
        {
            ShowModeSelectionDialog();
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Se si preme Enter, invia il messaggio
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Evita di aggiungere una nuova riga
                SendMessage(messageTextBox.Text);
                messageTextBox.Clear();
            }

            // Se si preme Ctrl+Backspace, cancella l'ultima parola
            if (e.KeyCode == Keys.Back && e.Control)
            {
                e.SuppressKeyPress = true;
                RemoveLastWord();
            }
        }

        private void ShowModeSelectionDialog()
        {
            var result = MessageBox.Show(
                "Vuoi avviare come Server?",
                "Modalità",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                string ip = PromptInput("Inserisci l'indirizzo IP del server (default: 0.0.0.0):", "0.0.0.0");
                int port = int.Parse(PromptInput("Inserisci la porta del server (default: 9999):", "9999"));
                SaveKeysToFile();
                SetupServer(ip, port);
            }
            else
            {
                string serverIp = PromptInput("Inserisci l'indirizzo IP del server:", "127.0.0.1");
                int serverPort = int.Parse(PromptInput("Inserisci la porta del server:", "9999"));
                LoadKeysFromFile();
                ConnectToServer(serverIp, serverPort);
            }

            if (result == DialogResult.Yes)
            {
                string ip = PromptInput("Inserisci l'indirizzo IP del server (default: 0.0.0.0):", "0.0.0.0");
                int port = int.Parse(PromptInput("Inserisci la porta del server (default: 9999):", "9999"));

                // Chiede all'utente se vuole riutilizzare le chiavi precedenti
                var useExistingKeys = MessageBox.Show(
                    "Vuoi usare le stesse chiavi di crittografia dell'ultima sessione?",
                    "Scelta delle chiavi",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (useExistingKeys == DialogResult.Yes)
                {
                    LoadKeysFromFile();
                    AppendToChat("Chiavi di crittografia caricate dal file.");
                }
                else
                {
                    GenerateNewKeys();
                    SaveKeysToFile();
                    AppendToChat("Nuove chiavi generate e salvate.");
                }

                SetupServer(ip, port);
            }

        }

        private string PromptInput(string message, string defaultValue)
        {
            Form prompt = new Form
            {
                Width = 400,
                Height = 200,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = "Input",
                StartPosition = FormStartPosition.CenterScreen
            };

            Label messageLabel = new Label
            {
                Left = 20,
                Top = 20,
                Text = message,
                AutoSize = true
            };

            TextBox inputBox = new TextBox
            {
                Left = 20,
                Top = 50,
                Width = 340,
                Text = defaultValue
            };

            Button confirmButton = new Button
            {
                Text = "OK",
                Left = 280,
                Width = 80,
                Top = 100,
                DialogResult = DialogResult.OK
            };

            prompt.Controls.Add(messageLabel);
            prompt.Controls.Add(inputBox);
            prompt.Controls.Add(confirmButton);
            prompt.AcceptButton = confirmButton;

            return prompt.ShowDialog() == DialogResult.OK ? inputBox.Text : defaultValue;
        }

        private void SaveKeysToFile()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter("shared_keys.txt"))
                {
                    // Convert AES key and HMAC key to Base64 and write them
                    string aesKeyBase64 = Convert.ToBase64String(aesKey);
                    string aesIVBase64 = Convert.ToBase64String(aesIV);
                    string hmacKeyBase64 = Convert.ToBase64String(hmacKey);

                    writer.WriteLine("[AES Key]");
                    writer.WriteLine(aesKeyBase64);

                    writer.WriteLine("[AES IV]");
                    writer.WriteLine(aesIVBase64);

                    writer.WriteLine("[HMAC Key]");
                    writer.WriteLine(hmacKeyBase64);

                    // Write RSA public key in XML format
                    string publicKeyXml = rsaProvider.ToXmlString(false);
                    writer.WriteLine("[RSA Public Key]");
                    writer.WriteLine(publicKeyXml);
                }

                MessageBox.Show("Chiavi di crittografia salvate nel file: shared_keys.txt", "Chiavi salvate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante il salvataggio delle chiavi: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GenerateNewKeys()
        {
            aesIV = GenerateRandomBytes(16);
            aesKey = GenerateRandomBytes(32);
            hmacKey = GenerateRandomBytes(32);
            rsaProvider = new RSACryptoServiceProvider(2048);
        }

        private void LoadKeysFromFile()
        {
            try
            {
                using (StreamReader reader = new StreamReader("shared_keys.txt"))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line == "[AES Key]")
                        {
                            string aesKeyBase64 = reader.ReadLine();
                            aesKey = Convert.FromBase64String(aesKeyBase64);
                        }
                        else if (line == "[AES IV]")
                        {
                            string aesIVBase64 = reader.ReadLine();
                            aesIV = Convert.FromBase64String(aesIVBase64);
                        }
                        else if (line == "[HMAC Key]")
                        {
                            string hmacKeyBase64 = reader.ReadLine();
                            hmacKey = Convert.FromBase64String(hmacKeyBase64);
                        }
                        else if (line == "[RSA Public Key]")
                        {
                            string publicKeyXml = reader.ReadLine();
                            rsaProvider.FromXmlString(publicKeyXml);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante il caricamento delle chiavi: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupServer(string ip, int port)
        {
            try
            {
                server = new TcpListener(IPAddress.Parse(ip), port);
                server.Start();
                AppendToChat($"Server avviato su {ip}:{port}");
                Thread acceptThread = new Thread(AcceptClients);
                acceptThread.Start();
            }
            catch (Exception ex)
            {
                AppendToChat($"Errore durante l'avvio del server: {ex.Message}");
            }
        }

        private void AcceptClients()
        {
            try
            {
                client = server.AcceptTcpClient();
                stream = client.GetStream();
                AppendToChat("Client connesso.");
                Thread receiveThread = new Thread(ReceiveMessages);
                receiveThread.Start();
            }
            catch (Exception ex)
            {
                AppendToChat($"Errore durante l'accettazione del client: {ex.Message}");
            }
        }

        private void ConnectToServer(string ip, int port)
        {
            try
            {
                client = new TcpClient(ip, port);
                stream = client.GetStream();
                AppendToChat($"Connesso al server {ip}:{port}");
                Thread receiveThread = new Thread(ReceiveMessages);
                receiveThread.Start();
            }
            catch (Exception ex)
            {
                AppendToChat($"Errore durante la connessione al server: {ex.Message}");
            }
        }

        private void AppendToChat(string message)
        {
            if (chatTextBox.InvokeRequired)
            {
                if (chatTextBox.IsHandleCreated)
                {
                    chatTextBox.Invoke(new Action(() => chatTextBox.AppendText(message + Environment.NewLine)));
                }
            }
            else
            {
                chatTextBox.AppendText(message + Environment.NewLine);
            }
        }

        private byte[] GenerateRandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            string message = messageTextBox.Text;
            SendMessage(message);
            messageTextBox.Clear();
        }

        private void ReceiveMessages()
        {
            while (true)
            {
                try
                {
                    byte[] buffer = new byte[BufferSize];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        byte[] receivedData = new byte[bytesRead];
                        Array.Copy(buffer, receivedData, bytesRead);

                        int encryptedMessageLength = receivedData.Length - 32; // HMAC-SHA-256 (32 bytes)
                        byte[] encryptedMessage = new byte[encryptedMessageLength];
                        byte[] receivedHMAC = new byte[32];

                        Array.Copy(receivedData, 0, encryptedMessage, 0, encryptedMessageLength);
                        Array.Copy(receivedData, encryptedMessageLength, receivedHMAC, 0, 32);

                        byte[] computedHMAC = ComputeHMAC(encryptedMessage, hmacKey);

                        if (!AreArraysEqual(receivedHMAC, computedHMAC))
                        {
                            AppendToChat($"Errore: HMAC non valido!");
                            AppendToChat($"HMAC ricevuto: {BitConverter.ToString(receivedHMAC)}");
                            AppendToChat($"HMAC calcolato: {BitConverter.ToString(computedHMAC)}");
                            continue;
                        }

                        byte[] decryptedMessage = DecryptMessageAES(encryptedMessage, aesKey, aesIV);
                        string message = Encoding.UTF8.GetString(decryptedMessage);
                        AppendToChat($"Peer: {message}");
                    }
                }
                catch (Exception ex)
                {
                    AppendToChat($"Errore durante la ricezione del messaggio: {ex.Message}");
                    break;
                }
            }
        }

        private void SendMessage(string message)
        {
            if (client == null || !client.Connected) return;

            try
            {
                // Step 1: Convert message to bytes
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);

                // Step 2: Encrypt message with AES
                byte[] encryptedMessage = EncryptMessageAES(messageBytes, aesKey, aesIV);

                // Step 3: Compute HMAC for integrity
                byte[] hmac = ComputeHMAC(encryptedMessage, hmacKey);

                // Step 4: Combine encrypted message and HMAC
                byte[] finalMessage = CombineArrays(encryptedMessage, hmac);

                // Step 5: Send the message
                stream.Write(finalMessage, 0, finalMessage.Length);

                // Append message to chat
                AppendToChat($"You: {message}");
            }
            catch (Exception ex)
            {
                AppendToChat($"Errore durante l'invio del messaggio: {ex.Message}");
            }
        }

        private byte[] EncryptMessageAES(byte[] message, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(message, 0, message.Length);
                }
            }
        }

        private byte[] DecryptMessageAES(byte[] encryptedMessage, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(encryptedMessage, 0, encryptedMessage.Length);
                }
            }
        }

        private byte[] ComputeHMAC(byte[] data, byte[] key)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(data);
            }
        }

        private byte[] CombineArrays(byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }
        private bool AreArraysEqual(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;

            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                    return false;
            }

            return true;
        }

        private void RemoveLastWord()
        {
            string text = messageTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;

            int pos = messageTextBox.SelectionStart; // Posizione del cursore
            if (pos == 0)
                return;

            int lastSpaceIndex = text.LastIndexOf(' ', pos - 1);
            if (lastSpaceIndex == -1)
                messageTextBox.Text = ""; // Cancella tutto se c'è una sola parola
            else
                messageTextBox.Text = text.Substring(0, lastSpaceIndex);

            messageTextBox.SelectionStart = messageTextBox.Text.Length; // Rimetti il cursore alla fine
        }

    }
}

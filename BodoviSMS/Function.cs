using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.Json;
using HtmlAgilityPack;
using ImapX;
using ImapX.Authentication;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(JsonSerializer))]

namespace BodoviSMS
{
    public class Function
    {
        public static HttpClient Client = new HttpClient();
        public static string CsrfToken = "";

        public void FunctionHandler(ILambdaContext context)
        {
            var provider = CodePagesEncodingProvider.Instance;
            Encoding.RegisterProvider(provider);
            const string mailEmail = "yourmail@mail.com";
            const string mailPassword = "mailPassword";

            const string bonbonEmail = "bonbonEmail@mail.com";
            const string bonbonPassword = "bonbonPassword";

            LambdaLogger.Log("Connecting to email client");
            var client = new ImapClient("imap.gmail.com", 993, SslProtocols.Tls);
            client.Connect();
            LambdaLogger.Log("Email Client Connected");
            client.Login(new PlainCredentials(mailEmail, mailPassword));
            LambdaLogger.Log("Email Client Authenticated");

            client.Folders.Inbox.Messages.Download();
            var messages = client.Folders.Inbox.Messages;
            var count = messages.Count();

            LambdaLogger.Log("Count : " + count);
            if (count <= 0) return;

            LambdaLogger.Log("Added Messages");
            InitClient(bonbonEmail, bonbonPassword);
            LambdaLogger.Log("Initialized HttpClient");
            for (var i = 0; i < count; i++)
            {
                SendBonbonSms(messages[i]);
                LambdaLogger.Log("Message sent");
                messages[i].Remove();
            }

            client.Dispose();
        }

        private static void InitClient(string bonbonEmail, string bonbonPassword)
        {
            Client.BaseAddress = new Uri($"https://www.bonbon.hr");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.92 Safari/537.36");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");

            var a = Client.GetAsync($"https://www.bonbon.hr/registracija/prijava");
            var doc = new HtmlDocument();
            var html = a.Result.Content.ReadAsStringAsync();
            doc.LoadHtml(html.Result);
            CsrfToken = doc.DocumentNode.SelectSingleNode("//input[@name='csrf_token']").Attributes["value"].Value;
            var contentList = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("email", bonbonEmail),
                new KeyValuePair<string, string>("password", bonbonPassword),
                new KeyValuePair<string, string>("lsend", "Prijavi se"),
                new KeyValuePair<string, string>("csrf_token", CsrfToken)
            };
            var content = new FormUrlEncodedContent(contentList);
            // ReSharper disable once UnusedVariable
            var ss = Client.PostAsync("https://www.bonbon.hr/registracija/prijava", content).Result;
            // ReSharper disable once UnusedVariable
            var res = Client.GetAsync("https://www.bonbon.hr/profil").Result;
        }

        private static void SendBonbonSms(Message mess)
        {
            var text = RemoveGarbage(mess.Body.Text).Replace("\n", "\\n");

            var txt = $@"{{
  ""msisdn"": ""phonenumber"",
  ""recipient_msisdn"": ""phonenumber"",
  ""recipient_message"": ""{text}"",
  ""f_msg_num"": {text.Length / 160 + 1},
  ""f_free_msg_num"": {text.Length / 160 + 1},
  ""f_charge_msg_num"": 0
}} ";

            Client.DefaultRequestHeaders.Referrer = new Uri("https://www.bonbon.hr/profil");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en,hr;q=0.9,en-US;q=0.8");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("X-bonbon-client", "portal");

            var content = new StringContent(txt, Encoding.UTF8, "application/json");
            var ss = Client.PostAsync("https://api2.bonbon.hr/sms/send", content).Result;
            // ReSharper disable once UnusedVariable
            var result = ss.Content.ReadAsStringAsync().Result;
        }

        private static string RemoveGarbage(string body)
        {
            var mess = body;
            mess = mess.Remove(mess.IndexOf("--", StringComparison.Ordinal)).Replace("\r", "");
            var lista = mess.Split(' ').ToList();
            lista.RemoveAll(i => i == "");
            var s = "";
            foreach (var t in lista) s += t + " ";
            lista = s.Split('\n').ToList();
            lista.RemoveAll(i => i == "" || i == " ");
            s = "";
            foreach (var t in lista) s += t + "\n";
            return s.TrimEnd(new[] { '\n' });
        }
    }
}
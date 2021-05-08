using System;
using System.Windows.Forms;
using Chummer;
using ChummerHub.Client;
using ChummerHub.Client.Backend;
using ChummerHub.Client.Properties;
using ChummerHub.Client.Sinners;
using Newtonsoft.Json;
using NLog;


namespace ChummerHub.Client.UI
{
    public partial class frmWebBrowser : Form
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public frmWebBrowser()
        {
            InitializeComponent();
        }

        private Uri LoginUrl
        {
            get
            {
                if(string.IsNullOrEmpty(Settings.Default.SINnerUrl))
                {
                    Settings.Default.SINnerUrl = "https://chummer-stable.azurewebsites.net/";
                    string msg = "if you are (want to be) a Beta-Tester, change this to http://chummer-beta.azurewebsites.net/!";
                    Log.Warn(msg);
                    Settings.Default.Save();
                }
                string path = Settings.Default.SINnerUrl.TrimEnd('/');

                path += "/Identity/Account/Login?returnUrl=/Identity/Account/Manage";
                return new Uri(path);
            }
        }

        private void frmWebBrowser_Load(object sender, EventArgs e)
        {
            Invoke((Action)(() =>
                {
                    SuspendLayout();
                    webBrowser2.Navigated += webBrowser2_Navigated;
                    webBrowser2.ScriptErrorsSuppressed = true;
                    webBrowser2.Navigate(LoginUrl);
                    BringToFront();
                })
                );
        }

        private bool login;

        private async void webBrowser2_Navigated(object sender, WebBrowserNavigatedEventArgs e)
        {
            if(e.Url.AbsoluteUri == LoginUrl.AbsoluteUri)
                return;
            if((e.Url.AbsoluteUri.Contains("/Identity/Account/Logout")))
            {
                //maybe we are logged in now
                GetCookieContainer();
            }
            else if (e.Url.AbsoluteUri.Contains("/Identity/Account/Manage"))
            {
                try
                {
                    //we are logged in!
                    GetCookieContainer();
                    var client = StaticUtils.GetClient();
                    if (client == null)
                    {
                        Log.Error("Cloud not create an instance of SINnersclient!");
                        return;
                    }
                    //var body = client.GetUserByAuthorizationAsync().Result;
                    var body = await client.GetUserByAuthorizationAsync().ConfigureAwait(false);
                    {
                     
                        if (body?.CallSuccess == true)
                        {
                            login = true;
                            Program.MainForm.Invoke(new Action(() =>
                            {
                                SINnerVisibility tempvis = Backend.Utils.DefaultSINnerVisibility
                                                           ?? new SINnerVisibility
                                {
                                    IsGroupVisible = true,
                                    IsPublic = true
                                };
                                tempvis.AddVisibilityForEmail(body.MyApplicationUser?.Email);
                                Close();
                            }));
                        }
                        else
                        {
                            login = false;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(exception);
                    throw;
                }
            }
        }

        private void GetCookieContainer()
        {
            try
            {
                using (new CursorWait(this, true))
                {
                    Settings.Default.CookieData = null;
                    Settings.Default.Save();
                    var cookies =
                        StaticUtils.AuthorizationCookieContainer?.GetCookies(new Uri(Settings.Default
                            .SINnerUrl));
                    var client = StaticUtils.GetClient(true);
                }
            }
            catch(Exception ex)
            {
                Log.Warn(ex);
            }
        }

        private void FrmWebBrowser_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (login == false)
                GetCookieContainer();
        }
    }
}

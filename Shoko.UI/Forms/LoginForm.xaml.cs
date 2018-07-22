﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using Nancy.Helpers;

namespace Shoko.UI.Forms
{
    /// <summary>
    /// Interaction logic for Page1.xaml
    /// </summary>
    public partial class LoginForm : Window
    {
        public const string AuthUrl = "{4}?client_id={0}&scope={1}&response_type={2}&redirect_uri={3}";
        public string Code { get; private set; }
        public List<string> Scopes { get; private set; } = new List<string>();
        private Uri uri;

        public LoginForm(string name, string authurl, string clientid, List<string> scopes, string redirect,
            bool scopecommaseparated)
        {
            InitializeComponent();
            this.Title = string.IsNullOrEmpty(name) ? "Login" : name;
            WebView.Navigated += WebView_Navigated;
            WebView.Navigating += WebView_Navigating;
            string responsetype = "code";
            string sep = scopecommaseparated ? "," : " ";
            string url = string.Format(AuthUrl, HttpUtility.UrlEncode(clientid),
                HttpUtility.UrlEncode(string.Join(sep, scopes)), HttpUtility.UrlEncode(responsetype),
                HttpUtility.UrlEncode(redirect), authurl);
            uri = new Uri(url);
            this.Visibility = Visibility.Visible;
        }

        private void WebView_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            CheckUrl(e.Uri.ToString());
        }

        private void WebView_Navigated(object sender, NavigationEventArgs e)
        {
            CheckUrl(e.Uri.ToString());
        }

        bool checke = false;

        private void CheckUrl(string url)
        {
            if (url.Contains("code=") && !checke)
            {
                int a = url.IndexOf("code=", StringComparison.Ordinal);

                string n = url.Substring(a);
                if (n.EndsWith("/"))
                    n = n.Substring(0, n.Length - 1);
                NameValueCollection col = HttpUtility.ParseQueryString(n);
                foreach (string s in col.Keys)
                {
                    switch (s)
                    {
                        case "code":
                            Code = col[s];
                            break;
                        case "scope":
                            Scopes = col[s].Split(' ').ToList();
                            break;
                    }
                }
                DialogResult = Code != string.Empty;
                checke = true;
                Close();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WebView.Navigate(uri);
        }
    }
}
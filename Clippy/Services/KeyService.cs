using Clippy.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Security.Credentials;

namespace Clippy.Services
{
    public class KeyService : IKeyService
    {
        private const string Name = "Key";
        private readonly string Resource;
        public KeyService(string resource = "R") => Resource = resource;
        private PasswordVault Vault = new PasswordVault();

        public string GetKey()
        {
            try
            {
                var credential = Vault.Retrieve(Resource, Name);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch
            {
                return "";
            }
        }

        public void SetKey(string key)
        {
            PasswordCredential existing = null;
            try { existing = Vault.Retrieve(Resource, Name); }
            catch (System.Runtime.InteropServices.COMException) { }
            if (existing != null) Vault.Remove(existing);
            if (!string.IsNullOrWhiteSpace(key)) Vault.Add(new PasswordCredential(Resource, Name, key.Trim()));
        }
    }
}

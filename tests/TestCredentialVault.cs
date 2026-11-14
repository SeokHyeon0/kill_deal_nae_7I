// Test-only credential adapter. Never compiled by Build-Windows.cmd.
using System;using System.Collections.Generic;
namespace PubgScore {
 internal static class CredentialVault {
  static readonly Dictionary<string,string> values=new Dictionary<string,string>();static readonly object gate=new object();
  internal static string Read(string target){lock(gate){string value;return values.TryGetValue(target,out value)?value:"";}}
  internal static void Save(string target,string uid,string secret){lock(gate)values[target]=secret;}
  internal static void Delete(string target){lock(gate)values.Remove(target);}
 }
}

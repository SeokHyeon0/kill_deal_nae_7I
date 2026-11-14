using System;using System.Threading.Tasks;using PubgScore;
internal sealed class TestGameClient : IGameClient
{
 readonly LocalStore store;ScoreState empty;public bool Allowed=true,Connected=true,Administrator;public long Generation,Session;public bool Running=true;long sequence;
 public string Mode="";public int Writes,Resets;public TestGameClient(string file){store=new LocalStore(file);}
 public GameView View(){return new GameView {Mode=Mode,State=empty==null?store.Snapshot():empty.Copy(),Uid="test-user",Connected=Connected,Authenticated=true,IsAdmin=Administrator,CanEdit=Allowed&&Connected&&Running,Running=Running,Session=Session,Generation=Generation,Sequence=sequence,Info=Connected?"공용 데이터 · 입력 가능":"연결 확인 중"};}
 public void Change(){sequence++;}public Task AutoLogin(bool admin){Administrator=admin;Connected=true;Change();return Task.FromResult(0);}public Task ConnectAnonymous(){Connected=true;Change();return Task.FromResult(0);}
 public Task ResetAll(long expected){if(!Administrator||Running||Generation!=expected)throw new UnauthorizedAccessException();int rounds=View().State.RoundCount;empty=new ScoreState();empty.Cells["roundCount"]=new CellValue {Value=rounds.ToString(),Version=0};Generation++;Resets++;Change();return Task.FromResult(0);}
 public Task<CellValue> Write(string key,string value,int version,long generation,long session=0){if(!View().CanEdit||generation!=Generation||session!=Session)throw new InvalidOperationException("stale");Writes++;CellValue saved;if(empty==null){var state=store.Apply(new ChangeRequest {Key=key,Value=value,Version=version,RequestId=Guid.NewGuid().ToString("N")});saved=state.Cells[key];}else {if(empty.Version(key)!=version)throw new ConflictException(empty.Get(key),empty.Version(key));saved=new CellValue {Value=value,Version=version+1};empty.Cells[key]=saved;}Change();return Task.FromResult(saved);}
 public Task LoginAdministrator(string email,string password){Administrator=true;Connected=true;Change();return Task.FromResult(0);}
 public void Logout(){Administrator=false;Connected=false;Change();}
 public Task SetRunning(bool running){if(!Administrator)throw new UnauthorizedAccessException();if(Running!=running){Running=running;Session++;Change();}return Task.FromResult(0);}
 public void Dispose(){}
}

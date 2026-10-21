using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LeaderboardVerification
{
    static LeaderboardVerification(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/verify-leaderboard-data.request"))return;
        File.Delete("Temp/verify-leaderboard-data.request");string path="Library/LeaderboardVerification/Data-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(path);
        try{Run(path);File.WriteAllText(path+"/result.txt",DateTime.UtcNow.ToString("O")+"\nPASS: independent ordering, Power ties, exact own rank outside top 50, snapshot query contract, malformed/failed reads, opt-out, cancellation and serialized bindings.");}
        catch(Exception e){File.WriteAllText(path+"/result.txt","FAIL: "+e);Debug.LogException(e);}
        File.WriteAllText("Temp/verify-leaderboard-data.result",path+"/result.txt");
    }
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    sealed class Http : IAccountHttp
    {
        public Queue<AccountHttpResult> responses=new Queue<AccountHttpResult>();public List<string> bodies=new List<string>();
        public bool cancelled;
        public IEnumerator Send(string url,string method,string body,string contentType,string bearer,Action<AccountHttpResult> complete){bodies.Add(body);complete(responses.Dequeue());yield break;}
        public void Cancel(){cancelled=true;}
        public void Add(string body,long code=200){responses.Enqueue(new AccountHttpResult{code=code,body=body});}
    }
    static void Drain(IEnumerator run)
    {
        var stack=new Stack<IEnumerator>();stack.Push(run);int count=0;
        while(stack.Count>0){Check(++count<1000,"Unbounded reader");if(!stack.Peek().MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(stack.Peek().Current is IEnumerator child)stack.Push(child);}
    }
    const string Time="2026-10-21T14:00:00.000001Z";
    static string Document(AccountAggregate row)
    {
        AccountRest.Value S(string s)=>new AccountRest.Value{stringValue=s};AccountRest.Value N(long n)=>new AccountRest.Value{integerValue=n.ToString()};
        return JsonUtility.ToJson(new AccountRest.Document{name=AccountRest.DocumentPath("demo-test",row.playerId),updateTime=Time,fields=new AccountRest.Fields{
            playerId=S(row.playerId),playerName=S(row.playerName),bossStage=N(row.bossStage),totalTasksCompleted=N(row.totalTasksCompleted),totalFocusMinutes=N(row.totalFocusMinutes),power=N(row.power),catLevel=N(row.catLevel),updatedAt=new AccountRest.Value{timestampValue=Time}}});
    }
    static string Rows(params AccountAggregate[] rows)=>"["+(rows.Length==0?"{\"readTime\":\""+Time+"\"}":string.Join(",",rows.Select(r=>"{\"document\":"+Document(r)+",\"readTime\":\""+Time+"\"}")))+"]";
    [Serializable] sealed class Contract{public string kind,top,own,rank;public long expectedRank;}
    [Serializable] sealed class Contracts{public Contract[] items;}
    public static void Run(string folder)
    {
        var a=new AccountAggregate{playerId="a",playerName="A",bossStage=4,totalTasksCompleted=3,totalFocusMinutes=7,power=20,catLevel=1};
        var b=new AccountAggregate{playerId="b",playerName="B",bossStage=3,totalTasksCompleted=9,totalFocusMinutes=2,power=20,catLevel=5};
        Check(LeaderboardQuery.Compare(a,b,LeaderboardKind.BossStage)<0,"Boss descending");Check(LeaderboardQuery.Compare(a,b,LeaderboardKind.TasksCompleted)>0,"Tasks independent");
        Check(LeaderboardQuery.Compare(a,b,LeaderboardKind.FocusMinutes)<0,"Focus independent");Check(LeaderboardQuery.Compare(a,b,LeaderboardKind.Power)>0,"Power level tie");
        a.catLevel=b.catLevel;Check(LeaderboardQuery.Compare(a,b,LeaderboardKind.Power)<0,"Power UID tie");
        b.bossStage=a.bossStage;b.catLevel=999;Check(LeaderboardQuery.Compare(a,b,LeaderboardKind.BossStage)<0,"Non-Power must ignore level");
        Check(LeaderboardQuery.Display(a,LeaderboardKind.BossStage)=="Master 1","Boss/master boundary");
        a.bossStage=3;Check(LeaderboardQuery.Display(a,LeaderboardKind.BossStage)=="Boss 3","Boss 3 display");
        var own=new AccountAggregate{playerId="own",playerName="Own",bossStage=1,totalTasksCompleted=0,totalFocusMinutes=0,power=0,catLevel=1};
        var contracts=new List<Contract>();
        foreach(LeaderboardKind kind in Enum.GetValues(typeof(LeaderboardKind)))
        {
            var http=new Http();http.Add(Rows(a));http.Add(Rows(own));http.Add("[{\"result\":{\"aggregateFields\":{\"ahead\":{\"integerValue\":\"137\"}}}}]");
            var result=new LeaderboardSnapshot{kind=kind};Drain(LeaderboardReader.Fetch(http,"demo-test","own","mock",true,result));
            Check(result.success&&result.ownRank==138&&result.own.playerId=="own","Outside top 50 own rank failed: " + result.message + " / " + result.ownMessage + " requests=" + http.bodies.Count + " body=" + Rows(a));
            Check(http.bodies[1].Contains("\"readTime\":\""+Time+"\"")&&http.bodies[2].Contains("\"readTime\":\""+Time+"\""),"Queries mix read snapshots");
            Check(http.bodies[2].Contains("\"before\":true")&&!http.bodies[2].Contains("\"limit\""),"Rank must count exclusive cursor without top limit");
            contracts.Add(new Contract{kind=kind.ToString(),top=http.bodies[0],own=http.bodies[1],rank=http.bodies[2],expectedRank=138});
            http=new Http();http.Add(Rows(a));result=new LeaderboardSnapshot{kind=kind};Drain(LeaderboardReader.Fetch(http,"demo-test","a","mock",true,result));Check(result.ownRank==1&&http.bodies.Count==1,"Top own row should not need extra reads");
            http=new Http();http.Add(Rows(a));result=new LeaderboardSnapshot{kind=kind};Drain(LeaderboardReader.Fetch(http,"demo-test","a","mock",false,result));Check(result.success&&result.own==null&&http.bodies.Count==1,"Opt-out showed rank or made extra reads");
        }
        var failing=new Http();failing.Add("{}",403);var failed=new LeaderboardSnapshot();Drain(LeaderboardReader.Fetch(failing,"demo-test","a","mock",true,failed));Check(!failed.success&&failed.ownRank==0,"Error fabricated rank");
        failing=new Http();failing.Add(Rows(a));failing.Add(Rows(own));failing.Add("{}",500);failed=new LeaderboardSnapshot();Drain(LeaderboardReader.Fetch(failing,"demo-test","own","mock",true,failed));Check(failed.success&&failed.own==null&&failed.ownMessage!=null,"Count failure hid leaders or fabricated own rank");
        Check(!LeaderboardQuery.ParseRows(Rows(a,a),"demo-test",out _,out _),"Duplicate IDs accepted");
        Check(!LeaderboardQuery.ParseRows(Rows(a),"different-project",out _,out _),"Foreign document path accepted");
        Check(!LeaderboardQuery.ParseRows("not json","demo-test",out _,out _),"Invalid JSON accepted");
        Check(!LeaderboardQuery.ParseRank("[{\"result\":{\"aggregateFields\":{\"ahead\":{\"integerValue\":\"-1\"}}}}]",out _),"Negative count accepted");
        Check(LeaderboardQuery.ParseRank("[{\"result\":{\"aggregateFields\":{\"ahead\":{\"integerValue\":\"2147483648\"}}}}]",out var big)&&big==2147483649,"Rank truncated to int32");
        var transport=new Http();new LeaderboardReader(null,transport).Cancel();Check(transport.cancelled,"Closing page did not cancel transport");
        var canvas=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Canvas.prefab");
        try{var nav=canvas.GetComponentInChildren<MainMenuNavigation>(true);var page=nav.pages[5];var ui=page.GetComponentInChildren<LeaderboardPanelUI>(true);Check(ui&&ui.categories.Length==4&&ui.ownRow&&ui.template&&ui.scroll,"Bindings missing");foreach(var t in page.GetComponentsInChildren<Transform>(true))Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"Missing script");}
        finally{PrefabUtility.UnloadPrefabContents(canvas);}
        File.WriteAllText(folder+"/query-contracts.json",JsonUtility.ToJson(new Contracts{items=contracts.ToArray()},true));
    }
}

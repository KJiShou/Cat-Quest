using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BossPageUI : MonoBehaviour
{
    public BossArenaPreview arena;
    public BossBattleActors actors;
    public RectTransform catHealth;
    public Slider bossBar;
    public TMP_Text bossTitle,bossValue,message,actionLabel,confirmText,catDamage,bossDamage;
    public Button action,stageButton,confirm,cancel;
    public GameObject confirmation,stagePicker;
    public Button[] stageChoices;
    public TMP_Text[] stageCaptions;
    public GameObject[] stageLocks;
    public Slider[] masteryBars;
    public TMP_Text[] masteryValues;
    public GameObject masterLock;
    public Button masterChoice,closeStages;
    public TMP_Text masterCaption;
    public GameObject[] resultPanels;
    public TMP_Text[] resultValues,resultDetails;
    public Button[] resultContinue;
    TaskService service;
    BattleAttempt attempt;
    BattleSimulation simulation;
    bool approaching,confirmStop,quitting,settlementPending;
    BattleOutcome pendingOutcome;
    int selected=1,master;
    PlayerHealthFeedback healthFeedback;
    PlayerHealthFeedback bossHealthFeedback;
    Vector2 bossHealthRestPosition;
    BattleDamageNumbers damageNumbers;
    float returnToIdleAt;
    bool resetPreview;
    public BattleSimulation Simulation => simulation;
    public bool IsBattling => attempt!=null && !settlementPending;
    public int SelectedBoss => selected;
    public void Awake()
    {
        healthFeedback=catHealth.GetComponent<PlayerHealthFeedback>();
        bossHealthFeedback=bossBar.GetComponent<PlayerHealthFeedback>();
        bossHealthRestPosition=((RectTransform)bossBar.transform).anchoredPosition;
        damageNumbers=GetComponent<BattleDamageNumbers>()??gameObject.AddComponent<BattleDamageNumbers>();
        damageNumbers.Initialize(catDamage);catDamage.gameObject.SetActive(false);bossDamage.gameObject.SetActive(false);
        service=GameDataManager.EnsureInstance().Tasks;
        action.onClick.AddListener(Action);
        stageButton.onClick.AddListener(()=>{RefreshStages();stagePicker.SetActive(true);});
        closeStages.onClick.AddListener(()=>stagePicker.SetActive(false));
        for(int i=0;i<stageChoices.Length;i++){int id=i+1;stageChoices[i].onClick.AddListener(()=>SelectStage(id,0));}
        masterChoice.onClick.AddListener(()=>{int n=service.Snapshot.masterStage;SelectStage(1+(n-1)%3,n);});
        confirm.onClick.AddListener(Confirm);
        cancel.onClick.AddListener(()=>confirmation.SetActive(false));
        foreach(var button in resultContinue)button.onClick.AddListener(CloseResult);
    }
    void OnEnable(){resetPreview=true;if(service!=null){service.Changed+=Refresh;Refresh();}}
    void Start(){EnsureActors();Refresh();}
    void EnsureActors(){if(arena.Stage)actors.Bind(arena.Stage);}
    public void SelectStage(int id,int stage=0)
    {
        if(attempt!=null)return;
        returnToIdleAt=0;resetPreview=false;selected=id;master=stage;EnsureActors();actors.Select(id);stagePicker.SetActive(false);message.text="";simulation=null;Refresh();
    }
    string Name => selected==1?"Cyclope":selected==2?"Orc":"Demon";
    void RefreshStages()
    {
        var data=service.Snapshot;
        for(int i=0;i<3;i++){
            int id=i+1;var p=data.bossProgress.Single(x=>x.bossId==id);
            string gate=service.BattleEntryError(id);
            if(gate=="Earn a ticket through Focus.")gate=null;
            bool locked=gate!=null;
            stageLocks[i].SetActive(locked);
            stageChoices[i].interactable=!locked;
            // Five wins per equipment milestone; keep the completed third milestone full.
            int progress=p.wins<=0?0:p.wins>=15?5:1+(p.wins-1)%5;
            masteryBars[i].SetValueWithoutNotify(progress);
            masteryValues[i].text=$"{progress}/5";
            stageCaptions[i].text=$"Stage {id} · {(id==1?"Cyclope":id==2?"Orc":"Demon")}\n"+(gate??"");
        }
        bool masterUnlocked=data.bossProgress.Single(p=>p.bossId==3).wins>0;
        masterChoice.interactable=masterUnlocked;masterLock.SetActive(!masterUnlocked);
        masterCaption.text="Master "+data.masterStage+(masterUnlocked?"":" · Defeat Demon");
    }
    void Refresh()
    {
        if(service==null)return;
        var c=attempt?.cat??service.CatStats;
        bossTitle.text=(master>0?$"Master {master} · ":"")+Name;
        if(attempt==null){
            if(returnToIdleAt>0 && simulation!=null)SetHealth(simulation.CatHp,simulation.Cat.hp,simulation.BossHp,simulation.Boss.hp,true);
            else {var b=BattleRules.Boss(selected,master);SetHealth(c.hp,c.hp,b.hp,b.hp);}
        }
        actionLabel.text=settlementPending?"Retry Save":attempt!=null?"Stop":"Challenge";
        stageButton.interactable=attempt==null;
    }
    public void Action()
    {
        if(settlementPending){Settle(pendingOutcome);return;}
        confirmStop=attempt!=null;
        if(!confirmStop){
            var error=service.BattleEntryError(selected,master);
            if(error!=null){message.text=error;return;}
            var b=BattleRules.Boss(selected,master);
            confirmText.text=$"{Name}\nHP {b.hp}   ATK {b.atk:0.#}   DEF {b.def:0.#}\nASPD {b.aspd:0.00}/s\nEnter for 1 ticket?";
        } else confirmText.text="Stop this battle?\nThe entry ticket stays spent.";
        confirmation.SetActive(true);
    }
    public void Confirm()
    {
        confirmation.SetActive(false);
        if(confirmStop){Settle(BattleOutcome.Stopped);return;}
        if(attempt!=null)return;
        EnsureActors();
        if(!actors.Ready){message.text="Battle scene is not ready.";return;}
        if(!service.BeginBattle(selected,master,out var entry)){message.text=service.LastError;return;}
        attempt=entry;simulation=new BattleSimulation(entry.cat,entry.boss);
        returnToIdleAt=0;resetPreview=false;SetHealth(entry.cat.hp,entry.cat.hp,entry.boss.hp,entry.boss.hp);
        damageNumbers.Clear();
        approaching=true;actors.ResetPose();actors.StartRun();message.text="";Refresh();
    }
    void Update()
    {
        if(attempt==null && (resetPreview || (returnToIdleAt>0 && Time.unscaledTime>=returnToIdleAt))) {
            EnsureActors();
            if(actors.Ready) {actors.ResetPose();resetPreview=false;returnToIdleAt=0;damageNumbers.Clear();Refresh();}
        }
        if(attempt==null||settlementPending)return;
        try {
            if(approaching){if(actors.Run(Time.unscaledDeltaTime)){approaching=false;actors.StartCombat(attempt.cat,attempt.boss);}return;}
            simulation.Advance(Time.unscaledDeltaTime,OnHit);
            if(simulation.Outcome!=BattleOutcome.Running)Settle(simulation.Outcome);
        } catch(Exception e){Debug.LogException(e);Settle(BattleOutcome.Error);}
    }
    void OnHit(BattleHit hit)
    {
        actors.Hit(hit.catAttacks);
        SetHealth(hit.catHp,attempt.cat.hp,hit.bossHp,attempt.boss.hp,true);
        damageNumbers.Spawn(hit.damage,!hit.catAttacks,hit.catAttacks?actors.Boss:actors.Cat,arena.StageCamera);
    }
    void SetHealth(int ch,int cm,int bh,int bm,bool animate=false)
    {
        if(healthFeedback)healthFeedback.SetHealth((float)ch/cm,animate);
        else catHealth.GetComponent<Slider>().SetValueWithoutNotify((float)ch/cm);
        if(bossHealthFeedback)bossHealthFeedback.SetHealth((float)bh/bm,animate);
        else bossBar.SetValueWithoutNotify((float)bh/bm);
        bossValue.text=$"{bh}/{bm}";
    }
    public void Settle(BattleOutcome outcome)
    {
        if(attempt==null)return;
        pendingOutcome=outcome;
        actors.Finish(outcome);
        if(!service.ResolveBattle(attempt.id,outcome)){settlementPending=true;message.text=service.LastError;Refresh();return;}
        var receipt=service.Snapshot.battles.Single(b=>b.id==attempt.id);
        var p=service.Snapshot.bossProgress.Single(x=>x.bossId==selected);
        message.text=receipt.outcome==BattleOutcome.Victory ? "Victory"+(receipt.experience>0?$" · +{receipt.experience} EXP":"")+(receipt.fishCoins>0?$" · +{receipt.fishCoins} Fish":"")+(master==0?$"\nMastery {p.wins}"+(p.pendingClaims>0?$" · {p.pendingClaims} equipment claim(s) saved":""):"\nNext Master stage unlocked") : receipt.outcome==BattleOutcome.Defeat?"Defeat · Train and try again":receipt.outcome==BattleOutcome.Stopped?"Battle stopped":"Battle interrupted · Ticket restored";
        if(isActiveAndEnabled && (receipt.outcome==BattleOutcome.Victory || receipt.outcome==BattleOutcome.Defeat)) {
            int index=receipt.outcome==BattleOutcome.Victory?0:1;
            foreach(var panel in resultPanels)panel.SetActive(false);
            resultValues[index].text=index==0
                ? (receipt.experience>0?$"+{receipt.experience} EXP":"Mastery +1")+(receipt.fishCoins>0?$"   +{receipt.fishCoins} Fish":"")
                : "Train and try again";
            resultDetails[index].text=index==0
                ? (master>0?"Next Master stage unlocked":$"Mastery {p.wins}"+(p.pendingClaims>0?$" · {p.pendingClaims} equipment claim(s) saved":""))
                : "No rewards · Progress kept";
            resultPanels[index].SetActive(true);resultPanels[index].transform.SetAsLastSibling();message.text="";
        }
        returnToIdleAt=Time.unscaledTime+2f;
        attempt=null;approaching=false;settlementPending=false;Refresh();
        if(simulation!=null)SetHealth(simulation.CatHp,simulation.Cat.hp,simulation.BossHp,simulation.Boss.hp,true);
    }
    public void CloseResult()
    {
        foreach(var panel in resultPanels)panel.SetActive(false);
        returnToIdleAt=0;resetPreview=true;message.text="";
    }
    void LateUpdate()
    {
        if(arena.TryGetCatHeadViewport(out var point)){
            catHealth.gameObject.SetActive(point.z>0&&point.x>0&&point.x<1&&point.y>0&&point.y<1);
            catHealth.anchorMin=catHealth.anchorMax=new Vector2(point.x,point.y);catHealth.anchoredPosition=new Vector2(0,14);
        }
        if(healthFeedback)healthFeedback.Tick(Time.unscaledDeltaTime);
        if(bossHealthFeedback){
            ((RectTransform)bossBar.transform).anchoredPosition=bossHealthRestPosition;
            bossHealthFeedback.Tick(Time.unscaledDeltaTime);
        }
    }
    void OnApplicationQuit(){quitting=true;if(attempt!=null)service.ResolveBattle(attempt.id,settlementPending?pendingOutcome:simulation!=null&&simulation.Outcome!=BattleOutcome.Running?simulation.Outcome:BattleOutcome.Error);}
    void OnDisable()
    {
        if(service!=null)service.Changed-=Refresh;
        if(!quitting&&attempt!=null)Settle(settlementPending?pendingOutcome:simulation!=null&&simulation.Outcome!=BattleOutcome.Running?simulation.Outcome:BattleOutcome.Stopped);
        if(resultPanels!=null)foreach(var panel in resultPanels)if(panel)panel.SetActive(false);
        if(confirmation)confirmation.SetActive(false);if(stagePicker)stagePicker.SetActive(false);
    }
}

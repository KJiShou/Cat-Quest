using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BossPageUI : MonoBehaviour
{
    public BossArenaPreview arena;
    public BossBattleActors actors;
    public BossBattleSkipUI skipUI;
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
    public Image[] masteryIcons;
    public Sprite masteryChestClosed,masteryChestOpen;
    public ChestRewardParticles[] masteryParticles;
    public Image confirmationPortrait;
    public Sprite[] bossPortraits;
    public TMP_Text confirmationTitle,confirmationCost;
    public TMP_Text[] confirmationStats;
    public GameObject confirmationStatGroup;
    public GameObject masterLock;
    public Button masterChoice,closeStages;
    public TMP_Text masterCaption;
    public GameObject[] resultPanels;
    public TMP_Text[] resultValues,resultDetails;
    public RewardFlightUI rewardFlights;
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
        for(int i=0;i<masteryParticles.Length;i++)masteryParticles[i].Bind(masteryIcons[i].rectTransform);
        service=GameDataManager.EnsureInstance().Tasks;
        action.onClick.AddListener(Action);
        stageButton.onClick.AddListener(()=>{RefreshStages();stagePicker.SetActive(true);});
        closeStages.onClick.AddListener(()=>stagePicker.SetActive(false));
        for(int i=0;i<stageChoices.Length;i++){int id=i+1;stageChoices[i].onClick.AddListener(()=>SelectStage(id,0));}
        masterChoice.onClick.AddListener(()=>{int n=service.Snapshot.masterStage;SelectStage(1+(n-1)%3,n);});
        confirm.onClick.AddListener(Confirm);
        cancel.onClick.AddListener(()=>{confirmation.SetActive(false);if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(action.gameObject);});
        foreach(var button in resultContinue)button.onClick.AddListener(CloseResult);
        for(int i=0;i<masteryBars.Length;i++){
            int boss=i+1;var frame=masteryBars[i].transform.parent;
            var button=frame.GetComponent<Button>()??frame.gameObject.AddComponent<Button>();
            button.targetGraphic=frame.GetComponent<Image>();button.targetGraphic.raycastTarget=true;
            button.onClick.AddListener(()=>{
                if(IsBattling)return;
                GetComponent<EquipmentPageUI>().OpenClaim(boss);
            });
        }
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
            if(masteryIcons!=null&&i<masteryIcons.Length&&masteryIcons[i])masteryIcons[i].sprite=p.pendingClaims>0?masteryChestOpen:masteryChestClosed;
            if(masteryParticles!=null&&i<masteryParticles.Length&&masteryParticles[i])masteryParticles[i].gameObject.SetActive(p.pendingClaims>0&&service.EquipmentUnlocked);
            masteryValues[i].text=$"{progress}/5"+(p.pendingClaims>0?" · Claim":"");
            stageCaptions[i].text=$"Stage {id} · {(id==1?"Cyclope":id==2?"Orc":"Demon")}\n"+(gate??"");
        }
        bool masterUnlocked=data.bossProgress.Single(p=>p.bossId==3).wins>0;
        masterChoice.interactable=masterUnlocked;masterLock.SetActive(!masterUnlocked);
        masterCaption.text="Master "+data.masterStage+(masterUnlocked?"":" · Defeat Demon");
    }
    void Refresh()
    {
        if(service==null)return;
        if(stagePicker.activeSelf)RefreshStages();
        var c=attempt?.cat??service.CatStats;
        bossTitle.text=(master>0?$"Master {master} · ":"")+Name;
        if(attempt==null){
            if(returnToIdleAt>0 && simulation!=null)SetHealth(simulation.CatHp,simulation.Cat.hp,simulation.BossHp,simulation.Boss.hp,true);
            else {var b=BattleRules.Boss(selected,master);SetHealth(c.hp,c.hp,b.hp,b.hp);}
        }
        actionLabel.text=settlementPending?"Retry Save":attempt!=null?"Stop":"Challenge";
        stageButton.interactable=attempt==null;
        if(skipUI)skipUI.RefreshState();
    }
    public void Action()
    {
        if(settlementPending){Settle(pendingOutcome);return;}
        confirmStop=attempt!=null;
        if(!confirmStop){
            var error=service.BattleEntryError(selected,master);
            if(error!=null){message.text=error;return;}
        }
        var b=BattleRules.Boss(selected,master);
        confirmationTitle.text=confirmStop?"Stop battle?":(master>0?$"Master {master} · ":"")+Name;
        confirmationPortrait.sprite=bossPortraits[selected-1];
        confirmationStatGroup.SetActive(true);
        confirmationStats[0].text=$"{b.hp}";confirmationStats[1].text=$"{b.atk:0.#}";
        confirmationStats[2].text=$"{b.def:0.#}";confirmationStats[3].text=$"{b.aspd:0.00}/s";
        confirmationCost.text=confirmStop?"Ticket already used":"1 Boss Ticket";
        confirmText.text=confirmStop?"The entry ticket stays spent.":"Ready to challenge?";
        confirm.GetComponentInChildren<TMP_Text>().text=confirmStop?"Stop battle":"Challenge";
        cancel.GetComponentInChildren<TMP_Text>().text=confirmStop?"Keep fighting":"Cancel";
        confirmation.SetActive(true);
        if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(cancel.gameObject);
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
        if(attempt==null||settlementPending||(skipUI&&skipUI.IsConfirming))return;
        try {
            if(approaching){if(actors.Run(Time.unscaledDeltaTime)){approaching=false;actors.StartCombat(attempt.cat,attempt.boss,attempt.equippedEquipment.FirstOrDefault(i=>EquipmentRules.Find(i.id)?.Slot==EquipmentSlot.Weapon)?.id);}return;}
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
        if(skipUI)skipUI.Dismiss();
        pendingOutcome=outcome;
        actors.Finish(outcome);
        bool saved;
        if(rewardFlights)rewardFlights.SetRewardOrigin(resultValues[0].rectTransform);
        try { saved=service.ResolveBattle(attempt.id,outcome); }
        finally { if(rewardFlights)rewardFlights.SetRewardOrigin(null); }
        if(!saved){settlementPending=true;message.text=service.LastError;Refresh();return;}
        var receipt=service.Snapshot.battles.Single(b=>b.id==attempt.id);
        message.text=receipt.outcome==BattleOutcome.Victory ? "Victory"+(receipt.experience>0?$" · +{receipt.experience} EXP":"") : receipt.outcome==BattleOutcome.Defeat?"Defeat · Train and try again":receipt.outcome==BattleOutcome.Stopped?"Battle stopped":"Battle interrupted · Ticket restored";
        if(isActiveAndEnabled && (receipt.outcome==BattleOutcome.Victory || receipt.outcome==BattleOutcome.Defeat)) {
            int index=receipt.outcome==BattleOutcome.Victory?0:1;
            foreach(var panel in resultPanels)panel.SetActive(false);
            resultValues[index].text=index==0 ? (receipt.experience>0?$"+{receipt.experience}":"") : "Train and try again";
            resultValues[index].transform.parent.gameObject.SetActive(index!=0||receipt.experience>0);
            resultDetails[index].text=index==0?"":"No rewards · Progress kept";
            resultDetails[index].gameObject.SetActive(index!=0);
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
    public void SkipBattle()
    {
        if(!IsBattling || simulation==null)return;
        try {
            // Advance the existing snapshot without playing each hit. Settlement
            // still recomputes and persists the authoritative result exactly once.
            simulation.Advance(120);
            Settle(simulation.Outcome);
        } catch(Exception e){Debug.LogException(e);Settle(BattleOutcome.Error);}
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
        if(skipUI)skipUI.Dismiss();
        if(service!=null)service.Changed-=Refresh;
        if(!quitting&&attempt!=null)Settle(settlementPending?pendingOutcome:simulation!=null&&simulation.Outcome!=BattleOutcome.Running?simulation.Outcome:BattleOutcome.Stopped);
        if(resultPanels!=null)foreach(var panel in resultPanels)if(panel)panel.SetActive(false);
        if(confirmation)confirmation.SetActive(false);if(stagePicker)stagePicker.SetActive(false);
    }
}

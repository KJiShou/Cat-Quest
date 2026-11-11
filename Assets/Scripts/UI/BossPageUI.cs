using System;
using System.Collections;
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
    public Sprite criticalIcon;
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
    public TMP_Text masterDescription,masterEntryLabel;
    public GameObject[] resultPanels;
    public TMP_Text[] resultValues,resultDetails;
    public RewardFlightUI rewardFlights;
    public TMP_Text resultFishValue;
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
    bool skippedPresentation;
    float victoryResultAt;
    bool returningFromResult;
    Coroutine returnRoutine;
    public Animator endTransition,startTransition;
    bool walkingOut,resultAwaitingConfirmation;
    System.Action leavePage;
    string leaveAttemptId,confirmationAttemptId;
    bool leaveConfirmed;
    TaskDialogKeyboard confirmationKeyboard;
    public bool IsReturningFromResult => returningFromResult;
    public BattleSimulation Simulation => simulation;
    public bool IsBattling => attempt!=null && !settlementPending;
    public int SelectedBoss => selected;
    public void Awake()
    {
        healthFeedback=catHealth.GetComponent<PlayerHealthFeedback>();
        bossHealthFeedback=bossBar.GetComponent<PlayerHealthFeedback>();
        bossHealthRestPosition=((RectTransform)bossBar.transform).anchoredPosition;
        damageNumbers=GetComponent<BattleDamageNumbers>()??gameObject.AddComponent<BattleDamageNumbers>();
        damageNumbers.Initialize(catDamage,criticalIcon);catDamage.gameObject.SetActive(false);bossDamage.gameObject.SetActive(false);
        for(int i=0;i<masteryParticles.Length;i++)masteryParticles[i].Bind(masteryIcons[i].rectTransform);
        service=GameDataManager.EnsureInstance().Tasks;
        action.onClick.AddListener(Action);
        stageButton.onClick.AddListener(ShowStages);
        closeStages.onClick.AddListener(()=>stagePicker.SetActive(false));
        for(int i=0;i<stageChoices.Length;i++){int id=i+1;stageChoices[i].onClick.AddListener(()=>SelectStage(id,0));}
        masterChoice.onClick.AddListener(()=>{int n=service.MasterStage;SelectStage(1+(n-1)%3,n);});
        confirm.onClick.AddListener(Confirm);
        cancel.onClick.AddListener(DismissConfirmation);
        foreach(var button in resultContinue)button.onClick.AddListener(CloseResult);
        for(int i=0;i<masteryBars.Length;i++){
            int boss=i+1;var frame=masteryBars[i].transform.parent;
            var button=frame.GetComponent<Button>()??frame.gameObject.AddComponent<Button>();
            button.targetGraphic=frame.GetComponent<Image>();button.targetGraphic.raycastTarget=true;
            button.onClick.AddListener(()=>{
                if(returningFromResult||IsBattling)return;
                GetComponent<EquipmentPageUI>().OpenClaim(boss);
            });
        }
    }
    void OnEnable(){resetPreview=true;arena.StageChanged+=ArenaChanged;if(service!=null){service.Changed+=Refresh;SelectHighestUnlockedStage();Refresh();}}
    void Start(){EnsureActors();Refresh();}
    void SelectHighestUnlockedStage()
    {
        int stage=service.MasterStage;
        int boss=1+(stage-1)%3;
        if(service.BossUnlockError(boss,stage)==null){SelectStage(boss,stage);return;}
        for(int id=3;id>=1;id--)
            if(service.BossUnlockError(id)==null){SelectStage(id);return;}
    }
    void EnsureActors(){if(arena.IsReady){actors.Bind(arena.Stage);arena.ShowMasterAura(actors.Boss,actors.Cat,selected,master>0);}}
    void ArenaChanged()
    {
        if(!isActiveAndEnabled||attempt!=null)return;
        if(arena.IsReady){EnsureActors();actors.Select(selected);arena.ShowMasterAura(actors.Boss,actors.Cat,selected,master>0);message.text="";}
        else message.text=arena.LoadError??"Loading arena…";
        Refresh();
    }
    bool SyncMasterSelection()
    {
        // Keep the completed stage visible until its result is dismissed.
        if(service==null||master<=0||attempt!=null)return false;
        int next=service.MasterStage;
        if(next==master)return false;
        SelectStage(1+(next-1)%3,next);
        return true;
    }
    public void SelectStage(int id,int stage=0)
    {
        if(resultAwaitingConfirmation||returningFromResult||attempt!=null||victoryResultAt>0)return;
        returnToIdleAt=0;resetPreview=false;selected=id;master=stage;actors.ResetPose();simulation=null;stagePicker.SetActive(false);arena.SelectStage(id);
    }
    string Name => selected==1?"Cyclope":selected==2?"Orc":"Demon";
    public void ShowStages() { if(resultAwaitingConfirmation||returningFromResult||IsBattling||victoryResultAt>0)return; RefreshStages(); stagePicker.SetActive(true); }
    void RefreshStages()
    {
        int stageNumber=service.MasterStage;
        for(int i=0;i<3;i++){
            int id=i+1;var p=service.BossMastery(id);
            string gate=service.BossUnlockError(id);
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
        bool masterUnlocked=service.BossUnlockError(1+(stageNumber-1)%3,stageNumber)==null;
        masterChoice.interactable=masterUnlocked;masterLock.SetActive(!masterUnlocked);
        masterCaption.text="Master "+stageNumber;
        if(masterDescription)masterDescription.text=masterUnlocked
            ? (stageNumber%3==1?"Cyclope":stageNumber%3==2?"Orc":"Demon")+" · Stronger bosses each round"
            : "Defeat Demon to unlock";
        if(masterEntryLabel)masterEntryLabel.text=masterUnlocked?"Enter  >":"Locked";
    }
    void Refresh()
    {
        if(service==null)return;
        if(stagePicker.activeSelf)RefreshStages();
        var c=attempt?.cat??service.CatStats;
        bossTitle.text=(master>0?$"Master {master} · ":"")+Name;
        if(attempt==null){
            if((returnToIdleAt>0||returningFromResult) && simulation!=null)SetHealth(simulation.CatHp,simulation.Cat.hp,simulation.BossHp,simulation.Boss.hp,true);
            else {var b=BattleRules.Boss(selected,master);SetHealth(c.hp,c.hp,b.hp,b.hp);}
        }
        actionLabel.text=settlementPending?"Retry Save":attempt!=null?"Stop":arena.IsLoading?"Loading…":arena.LoadError!=null?"Retry Load":"Challenge";
        stageButton.interactable=!resultAwaitingConfirmation&&!returningFromResult&&attempt==null&&victoryResultAt<=0;
        action.interactable=!resultAwaitingConfirmation&&!returningFromResult&&victoryResultAt<=0&&(attempt!=null||arena.IsReady||arena.LoadError!=null);
        if(skipUI)skipUI.RefreshState();
    }
    public void Action()
    {
        if(resultAwaitingConfirmation||returningFromResult||victoryResultAt>0)return;
        if(settlementPending){Settle(pendingOutcome);return;}
        if(attempt==null&&!arena.IsReady){if(arena.LoadError!=null)arena.SelectStage(selected);else message.text="Loading arena…";return;}
        if(confirmation.activeSelf)return;
        ClearLeaveRequest();
        confirmStop=attempt!=null;
        confirmationAttemptId=attempt?.id;
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
        confirmText.text=confirmStop?"The entry ticket stays spent.":"";
        confirm.GetComponentInChildren<TMP_Text>().text=confirmStop?"Stop battle":"Challenge";
        cancel.GetComponentInChildren<TMP_Text>().text=confirmStop?"Keep fighting":"Cancel";
        confirmation.SetActive(true);
        confirmationKeyboard=confirmation.GetComponent<TaskDialogKeyboard>()??confirmation.AddComponent<TaskDialogKeyboard>();
        confirmationKeyboard.Open(new[]{cancel,confirm},DismissConfirmation);
    }
    public bool PrepareToLeavePage(System.Action continueNavigation)
    {
        if(attempt==null)return true;
        if(settlementPending){
            message.text=(service.LastError??"Battle result has not been saved.")+" Retry Save before leaving.";
            Refresh();return false;
        }
        // The first requested destination owns this prompt. Repeated navigation
        // cannot replace its action or confirm a different attempt.
        if(confirmation.activeSelf)return false;
        Action();
        if(confirmation.activeSelf&&confirmStop){leavePage=continueNavigation;leaveAttemptId=attempt.id;}
        return false;
    }
    void ClearLeaveRequest()
    {
        leavePage=null;leaveAttemptId=null;leaveConfirmed=false;
    }
    void DismissConfirmation()
    {
        ClearLeaveRequest();confirmationAttemptId=null;
        confirmation.SetActive(false);
        if(isActiveAndEnabled&&UnityEngine.EventSystems.EventSystem.current)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(action.gameObject);
    }
    public void Confirm()
    {
        if(!confirmation.activeSelf||resultAwaitingConfirmation||returningFromResult||victoryResultAt>0)return;
        if(confirmStop&&(attempt==null||attempt.id!=confirmationAttemptId)){DismissConfirmation();return;}
        confirmation.SetActive(false);
        confirmationAttemptId=null;
        if(confirmStop){
            leaveConfirmed=leavePage!=null&&leaveAttemptId==attempt.id;
            Settle(settlementPending?pendingOutcome:simulation!=null&&simulation.Outcome!=BattleOutcome.Running?simulation.Outcome:BattleOutcome.Stopped);
            return;
        }
        if(attempt!=null)return;
        EnsureActors();
        if(!arena.IsReady||!actors.Ready){message.text="Battle scene is not ready. No ticket was used.";return;}
        if(!service.BeginBattle(selected,master,out var entry)){message.text=service.LastError;return;}
        SettingsAudio.Instance?.SetBattleMusic(true);
        attempt=entry;simulation=new BattleSimulation(entry);
        skippedPresentation=false;
        returnToIdleAt=0;resetPreview=false;SetHealth(entry.cat.hp,entry.cat.hp,entry.boss.hp,entry.boss.hp);
        damageNumbers.Clear();
        approaching=true;actors.ResetPose();actors.StartRun();message.text="";Refresh();
    }
    void Update()
    {
        if(walkingOut&&actors.Exit(Time.unscaledDeltaTime))walkingOut=false;
        if(victoryResultAt>0&&Time.unscaledTime>=victoryResultAt&&!actors.IsFinishingVictory){
            // Let the boss finish falling before the cat starts its exit run.
            if(actors.Ready){actors.StartExit(arena.StageCamera);walkingOut=true;}
            victoryResultAt=0;ShowResultPanel(0);Refresh();
        }
        if(!resultAwaitingConfirmation && !returningFromResult && attempt==null && (resetPreview || (returnToIdleAt>0 && Time.unscaledTime>=returnToIdleAt))) {
            EnsureActors();
            if(actors.Ready) {actors.ResetPose();resetPreview=false;returnToIdleAt=0;damageNumbers.Clear();Refresh();}
        }
        if(attempt==null||settlementPending)return;
        try {
            if(approaching){if(actors.Run(Time.unscaledDeltaTime)){approaching=false;actors.StartCombat(attempt.cat,attempt.boss,attempt.equippedEquipment.FirstOrDefault(i=>EquipmentRules.Find(i.id)?.Slot==EquipmentSlot.Weapon)?.id);}return;}
            simulation.Advance(Time.unscaledDeltaTime,OnHit);
            if(simulation.Outcome!=BattleOutcome.Running)Settle(simulation.Outcome);
        } catch(Exception e){Debug.LogException(e);Settle(BattleOutcome.Error);}
    }
    void OnHit(BattleHit hit)
    {
        if(!hit.blocked)actors.Hit(hit.catAttacks);
        SettingsAudio.Instance?.PlayAttack(hit.catAttacks);
        SetHealth(hit.catHp,attempt.cat.hp,hit.bossHp,attempt.boss.hp,true);
        damageNumbers.Spawn(hit.damage,!hit.catAttacks,hit.catAttacks?actors.Boss:actors.Cat,arena.StageCamera,hit.critical);
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
        float presentationDelay=outcome==BattleOutcome.Victory&&!skippedPresentation&&isActiveAndEnabled&&actors.Ready?BossBattleActors.VictoryDuration:0;
        bool saved;
        if(rewardFlights)rewardFlights.SetRewardOrigin(resultValues[0].rectTransform, resultFishValue ? resultFishValue.rectTransform : null,presentationDelay);
        try { saved=service.ResolveBattle(attempt.id,outcome); }
        finally { if(rewardFlights)rewardFlights.SetRewardOrigin(null); }
        if(!saved){
            if(!leaveConfirmed)DismissConfirmation();
            actors.Finish(BattleOutcome.Error);settlementPending=true;message.text=service.LastError;Refresh();return;
        }
        var receipt=service.BattleReceipt(attempt.id);
        var continueNavigation=leaveConfirmed&&leaveAttemptId==attempt.id?leavePage:null;
        // A natural finish while the prompt is open invalidates its stop action.
        // Clear before invoking navigation so repeated confirmation cannot replay it.
        DismissConfirmation();
        if(receipt.outcome==BattleOutcome.Victory&&presentationDelay>0){
            actors.CelebrateVictory();victoryResultAt=Time.unscaledTime+presentationDelay;
            arena.ShowMasterAura(actors.Boss,actors.Cat,selected,false);
        }else actors.Finish(receipt.outcome);
        SettingsAudio.Instance?.SetBattleMusic(false);
        message.text=receipt.outcome==BattleOutcome.Victory ? "Victory"+(receipt.experience>0?$" · +{receipt.experience} EXP":"") : receipt.outcome==BattleOutcome.Defeat?"Defeat · Train and try again":receipt.outcome==BattleOutcome.Stopped?"Battle stopped":"Battle interrupted · Ticket restored";
        if(isActiveAndEnabled && (receipt.outcome==BattleOutcome.Victory || receipt.outcome==BattleOutcome.Defeat)) {
            resultAwaitingConfirmation=true;
            if(actors.Ready&&victoryResultAt<=0){actors.StartExit(arena.StageCamera);walkingOut=true;}
            int index=receipt.outcome==BattleOutcome.Victory?0:1;
            foreach(var panel in resultPanels)panel.SetActive(false);
            resultValues[index].text=index==0 ? (receipt.experience>0?$"+{receipt.experience}":"") : "Train and try again";
            resultValues[index].transform.parent.gameObject.SetActive(index!=0||receipt.experience>0);
            if(resultFishValue) {
                resultFishValue.text=receipt.fishCoins>0?$"+{receipt.fishCoins}":"";
                resultFishValue.transform.parent.gameObject.SetActive(index==0&&receipt.fishCoins>0);
            }
            resultDetails[index].text=index==0?"":"No rewards · Progress kept";
            resultDetails[index].gameObject.SetActive(index!=0);
            if(victoryResultAt<=0)ShowResultPanel(index);
            message.text="";
        }
        returnToIdleAt=receipt.outcome==BattleOutcome.Victory?float.PositiveInfinity:Time.unscaledTime+2f;
        attempt=null;approaching=false;settlementPending=false;Refresh();
        if(simulation!=null)SetHealth(simulation.CatHp,simulation.Cat.hp,simulation.BossHp,simulation.Boss.hp,true);
        if(isActiveAndEnabled)continueNavigation?.Invoke();
    }
    public void CloseResult()
    {
        if(returningFromResult||attempt!=null||victoryResultAt>0)return;
        if(!resultPanels.Any(panel=>panel.activeSelf))return;
        foreach(var panel in resultPanels)panel.SetActive(false);
        resultAwaitingConfirmation=false;
        returnToIdleAt=0;resetPreview=false;message.text="";
        returningFromResult=true;Refresh();
        returnRoutine=StartCoroutine(ReturnFromResult());
    }
    IEnumerator ReturnFromResult()
    {
        // The result is already committed. This sequence never starts or settles an entry.
        yield return PlayTransition(endTransition);
        // Switch only once the authored closing wipe fully covers the arena.
        walkingOut=false;returningFromResult=false;
        if(!SyncMasterSelection()){
            actors.ResetPose();EnsureActors();simulation=null;Refresh();
        }
        returningFromResult=true;Refresh();
        if(startTransition){
            startTransition.gameObject.SetActive(true);
            startTransition.transform.SetAsLastSibling();
            startTransition.updateMode=AnimatorUpdateMode.UnscaledTime;
            startTransition.Rebind();startTransition.Update(0);
        }
        if(endTransition)endTransition.gameObject.SetActive(false);
        yield return PlayTransition(startTransition);
        if(startTransition)startTransition.gameObject.SetActive(false);
        returningFromResult=false;returnRoutine=null;Refresh();
        if(UnityEngine.EventSystems.EventSystem.current)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(action.gameObject);
    }
    static IEnumerator PlayTransition(Animator animator)
    {
        if(!animator)yield break;
        animator.gameObject.SetActive(true);animator.transform.SetAsLastSibling();
        animator.updateMode=AnimatorUpdateMode.UnscaledTime;animator.Rebind();animator.Update(0);
        float duration=animator.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSecondsRealtime(duration);
    }
    void CancelReturn()
    {
        if(!returningFromResult)return;
        if(returnRoutine!=null)StopCoroutine(returnRoutine);
        returnRoutine=null;returningFromResult=false;
        if(endTransition)endTransition.gameObject.SetActive(false);
        if(startTransition)startTransition.gameObject.SetActive(false);
        walkingOut=false;actors.ResetPose();resetPreview=true;
    }
    public void SkipBattle()
    {
        if(!IsBattling || simulation==null)return;
        skippedPresentation=true;
        try {
            // Advance the existing snapshot without playing each hit. Settlement
            // still recomputes and persists the authoritative result exactly once.
            simulation.Advance(120);
            Settle(simulation.Outcome);
        } catch(Exception e){Debug.LogException(e);Settle(BattleOutcome.Error);}
    }
    void ShowResultPanel(int index)
    {
        resultPanels[index].SetActive(true);resultPanels[index].transform.SetAsLastSibling();
        var keyboard=resultPanels[index].GetComponent<TaskDialogKeyboard>();
        if(keyboard)keyboard.Open(new[]{resultContinue[index]},CloseResult);
        SettingsAudio.Instance?.PlayBattleResult(index==0?BattleOutcome.Victory:BattleOutcome.Defeat);
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
    void OnApplicationFocus(bool focused)
    {
        if(focused)return;
        if(confirmation.activeSelf)DismissConfirmation();else ClearLeaveRequest();
        if(!returningFromResult)return;
        CancelReturn();SyncMasterSelection();Refresh();
    }
    void OnApplicationQuit(){quitting=true;if(attempt!=null)service.ResolveBattle(attempt.id,settlementPending?pendingOutcome:simulation!=null&&simulation.Outcome!=BattleOutcome.Running?simulation.Outcome:BattleOutcome.Error);}
    void OnDisable()
    {
        arena.StageChanged-=ArenaChanged;
        ClearLeaveRequest();confirmationAttemptId=null;
        CancelReturn();walkingOut=false;resultAwaitingConfirmation=false;
        victoryResultAt=0;
        SettingsAudio.Instance?.SetBattleMusic(false);
        if(service!=null)service.Changed-=Refresh;
        if(!quitting&&attempt!=null)Settle(settlementPending?pendingOutcome:simulation!=null&&simulation.Outcome!=BattleOutcome.Running?simulation.Outcome:BattleOutcome.Stopped);
        if(resultPanels!=null)foreach(var panel in resultPanels)if(panel)panel.SetActive(false);
        if(confirmation)confirmation.SetActive(false);if(stagePicker)stagePicker.SetActive(false);
    }
}

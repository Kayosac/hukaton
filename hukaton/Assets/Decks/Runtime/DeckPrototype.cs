using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Character;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Decks.Unity
{
    

    /// <summary>Workshop presentation. The authored world owns expedition input, physics and returning.</summary>
    public sealed class DeckPrototype : MonoBehaviour
    {
        [Header("Saved content and ownership")]
        public DeckPrototypeConfig config;
        public PlayerControlGate control;
        public Hakaton.Economy.Unity.CoinWalletHost walletHost;
        public bool persistProgress=true;
        public AudioSource audioSource;
        public Canvas presentation;
        public TMP_Text goodsLabel;
        public event Action Departed;
        [Header("Embedded shop service — optional")]
        public bool embeddedMode;
        public CanvasGroup embeddedVisibility;
        public Button embeddedCloseButton;
        [Header("Shared shell")]
        public GameObject workshopPanel;
        public TMP_Text pageTitle, eyebrow, wallet, feedback;
        [Header("Workshop")]
        public Button explorationTab, combatTab, collectionTab, marketTab, previousPage, nextPage, equipButton, upgradeButton, handUpgradeButton, departButton;
        public TMP_Text explorationTabText, combatTabText, collectionHeading, pageNumber, deckCount, handSize, handUpgradeText, equipText, upgradeText, departText;
        public DeckCardView[] collectionViews;
        public DeckRowView[] deckRows;
        public Image detailArt, detailAccent;
        public TMP_Text detailTitle, detailIdentity, detailPower, detailDescription;
        public DeckWorkshop Model {get; private set;}
        public bool Busy {get; private set;}
        public DeckKind WorkshopKind {get; private set;}
        public bool Market {get; private set;}
        public int SelectedCopyId {get; private set;}
        public string SelectedDefinitionId {get; private set;}
        public bool EmbeddedOpen {get; private set;}
        public event Action EmbeddedClosed;
        bool embeddedMarketOnly;
        bool CanUseWorkshop => !embeddedMode || EmbeddedOpen;
        int page;
        string savePath;
        readonly Color gold=new Color(.86f,.72f,.43f), cream=new Color(.91f,.90f,.81f), muted=new Color(.55f,.63f,.57f);

        void Awake()
        {
            Model=config.CreateWorkshop(walletHost ? walletHost.Wallet : null);
            savePath=Path.Combine(Application.persistentDataPath,"DeckPrototype","world-workshop-v2.json");
            string warning=null;
            if(persistProgress && !embeddedMode && !walletHost) DeckSaveStore.Load(savePath,Model,out warning);
            explorationTab.onClick.AddListener(()=>ChooseKind(DeckKind.Exploration));
            combatTab.onClick.AddListener(()=>ChooseKind(DeckKind.Combat));
            collectionTab.onClick.AddListener(()=>ChooseMarket(false));
            marketTab.onClick.AddListener(()=>ChooseMarket(true));
            previousPage.onClick.AddListener(()=>ChangePage(-1)); nextPage.onClick.AddListener(()=>ChangePage(1));
            equipButton.onClick.AddListener(EquipOrBuy); upgradeButton.onClick.AddListener(UpgradeSelected);
            handUpgradeButton.onClick.AddListener(UpgradeHand); departButton.onClick.AddListener(Depart);
            foreach(var item in collectionViews) {var view=item;view.button.onClick.AddListener(()=>SelectTile(view));}
            foreach(var item in deckRows) {var row=item;row.button.onClick.AddListener(()=>SelectCopy(row.CopyId));}
            ChooseKind(DeckKind.Exploration);
            SetFeedback(warning??"Выберите карту. Каждый экземпляр берётся в путь отдельно.",warning!=null);
            if(embeddedMode)
            {
                if(!embeddedVisibility || !embeddedCloseButton)
                {
                    Debug.LogError("An embedded deck workshop needs its authored visibility group and close button.",this);
                    enabled=false;
                    return;
                }
                embeddedCloseButton.onClick.AddListener(CloseEmbedded);
                SetEmbeddedVisibility(false);
            }
        }
        void OnEnable() {if(control)control.SetBlocked(this,!embeddedMode || EmbeddedOpen);}
        void OnDisable()
        {
            if(embeddedMode) CloseEmbedded();
            if(control)control.SetBlocked(this,false);
        }
        void Update()
        {
            if(embeddedMode && EmbeddedOpen && Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CloseEmbedded();
        }
        /// <summary>Opens the saved workshop as a world service while retaining the shared card collection.</summary>
        public bool OpenEmbedded(bool market=false)
        {
            if(!embeddedMode || !isActiveAndEnabled || Model==null || Model.IsAway || Busy
                || !embeddedVisibility || !embeddedCloseButton) return false;
            embeddedMarketOnly=market;
            EmbeddedOpen=true;
            Market=market;
            page=0;
            if(control)control.SetBlocked(this,true);
            SetEmbeddedVisibility(true);
            ChooseKind(WorkshopKind);
            SetFeedback(market?"Выберите карту: купленная копия останется в коллекции.":"Соберите колоды и улучшите нужные экземпляры или руку.");
            return true;
        }
        public void CloseEmbedded()
        {
            if(!embeddedMode)return;
            bool wasOpen=EmbeddedOpen;
            EmbeddedOpen=false;
            SetEmbeddedVisibility(false);
            if(control)control.SetBlocked(this,false);
            if(wasOpen)EmbeddedClosed?.Invoke();
        }
        void SetEmbeddedVisibility(bool visible)
        {
            if(!embeddedVisibility)return;
            embeddedVisibility.alpha=visible?1:0;
            embeddedVisibility.interactable=visible;
            embeddedVisibility.blocksRaycasts=visible;
        }
        string KindName(DeckKind kind) => kind==DeckKind.Exploration?"Исследование":"Бой";
        CardDefinition Definition(CardCopy copy) => copy==null?null:config.Find(copy.Spec.Id);
        Sprite Artwork(CardCopy copy) => copy.IsReturn?config.returnArtwork:Definition(copy).artwork;
        void Sound(AudioClip clip) {if(clip && audioSource)audioSource.PlayOneShot(clip,.65f);}
        public void SetFeedback(string text,bool error=false) {feedback.text=text;feedback.color=error?new Color(.96f,.56f,.39f):muted;}
        void Persist()
        {
            if(persistProgress && !embeddedMode && !walletHost && !DeckSaveStore.Save(savePath,Model,out var error))SetFeedback(error,true);
        }
        void Result(string error,string success,AudioClip sound)
        {
            SetFeedback(error??success,error!=null);
            if(error==null) {Sound(sound);Persist();}
            RefreshWorkshop();
        }
        public void ChooseKind(DeckKind kind)
        {
            if(Model.IsAway || Busy)return;
            WorkshopKind=kind;page=0;
            SelectedCopyId=Model.Owned.FirstOrDefault(c=>c.Spec.Kind==kind)?.Id??0;
            SelectedDefinitionId=config.cards.First(c=>c.kind==kind).id;
            RefreshWorkshop();
        }
        public void ChooseMarket(bool market)
        {
            if(Model.IsAway || Busy || (embeddedMode && EmbeddedOpen && market!=embeddedMarketOnly))return;
            Market=market;page=0;RefreshWorkshop();
        }
        void ChangePage(int delta) {page=Mathf.Max(0,page+delta);RefreshWorkshop();}
        void SelectTile(DeckCardView tile)
        {
            if(Busy || Model.IsAway)return;
            SelectedCopyId=tile.CopyId;SelectedDefinitionId=tile.DefinitionId;
            Sound(config.cardSound);RefreshWorkshop();
        }
        public void SelectCopy(int id)
        {
            var copy=Model.Find(id);if(copy==null || Busy || Model.IsAway || (embeddedMode && embeddedMarketOnly))return;
            Market=false;WorkshopKind=copy.Spec.Kind;SelectedCopyId=id;SelectedDefinitionId=copy.Spec.Id;
            page=Model.Owned.Where(c=>c.Spec.Kind==WorkshopKind).ToList().FindIndex(c=>c.Id==id)/collectionViews.Length;
            RefreshWorkshop();
        }
        public void EquipOrBuy()
        {
            if(!CanUseWorkshop || Busy || Model.IsAway)return;
            if(Market)
            {
                string error=Model.Buy(SelectedDefinitionId,out var copy);
                if(copy!=null && !(embeddedMode && embeddedMarketOnly)) {SelectCopy(copy.Id);}
                Result(error,"Новая копия в коллекции. Теперь её можно взять в путь.",config.coinSound);
            }
            else Result(Model.Toggle(SelectedCopyId),"Состав колоды сохранён.",config.cardSound);
        }
        public void UpgradeSelected()
        {
            if(!CanUseWorkshop || Busy || Market || Model.IsAway)return;
            Result(Model.Upgrade(SelectedCopyId),"Улучшен только выбранный экземпляр.",config.coinSound);
        }
        public void UpgradeHand()
        {
            if(!CanUseWorkshop || Busy || Model.IsAway || (embeddedMode && embeddedMarketOnly))return;
            Result(Model.ImproveHand(WorkshopKind),"В этой руке стало на одно место больше.",config.coinSound);
        }
        public void RefreshWorkshop()
        {
            if(Model==null || Model.IsAway)return;
            workshopPanel.SetActive(true);
            if(goodsLabel)goodsLabel.text="В лавке: "+Model.Goods.Sum(g=>g.count)+" предметов добычи";
            pageTitle.text=embeddedMode && embeddedMarketOnly?"Карточник":"Колоды";
            eyebrow.text=embeddedMode?(embeddedMarketOnly?"ГОБЛИН / ПОКУПКА КАРТ":"МАГИЧЕСКИЙ КОМПЬЮТЕР / ПОДГОТОВКА"):"ЛАВКА ДИКОВИН / ПОДГОТОВКА";
            wallet.text=Model.Coins.ToString();
            if(embeddedMode)
            {
                collectionTab.gameObject.SetActive(!embeddedMarketOnly);
                marketTab.gameObject.SetActive(embeddedMarketOnly);
                handUpgradeButton.gameObject.SetActive(!embeddedMarketOnly);
                upgradeButton.gameObject.SetActive(!embeddedMarketOnly);
                departButton.gameObject.SetActive(false);
            }
            explorationTabText.text="Исследование   "+Model.Selected(DeckKind.Exploration).Count;
            combatTabText.text="Бой   "+Model.Selected(DeckKind.Combat).Count;
            TintTab(explorationTab,WorkshopKind==DeckKind.Exploration);TintTab(combatTab,WorkshopKind==DeckKind.Combat);
            TintTab(collectionTab,!Market);TintTab(marketTab,Market);
            var copies=Model.Owned.Where(c=>c.Spec.Kind==WorkshopKind).ToList();
            var offers=config.cards.Where(c=>c.kind==WorkshopKind).ToList();
            int count=Market?offers.Count:copies.Count;
            int pages=Mathf.Max(1,Mathf.CeilToInt((float)count/collectionViews.Length));page=Mathf.Clamp(page,0,pages-1);
            collectionHeading.text=Market?"Карточная лавка":"Моя коллекция";
            pageNumber.text=$"{page+1} / {pages}";previousPage.interactable=page>0;nextPage.interactable=page+1<pages;
            for(int i=0;i<collectionViews.Length;i++)
            {
                int index=page*collectionViews.Length+i;var view=collectionViews[i];
                if(index>=count) {view.gameObject.SetActive(false);continue;}
                if(Market) {
                    var offer=offers[index];view.Bind(offer.artwork,offer.displayName,offer.purchasePrice+" монет",0,offer.id,SelectedDefinitionId==offer.id,false);
                } else {
                    var copy=copies[index];view.Bind(Artwork(copy),$"№ {copy.Id:00}  /  ур. {copy.Level}",
                        Model.IsSelected(copy.Id)?"В колоде":"В коллекции",copy.Id,copy.Spec.Id,SelectedCopyId==copy.Id,Model.IsSelected(copy.Id));
                }
            }
            var equipped=Model.Selected(WorkshopKind);deckCount.text=$"{equipped.Count:00} / {config.deckLimit:00}";
            for(int i=0;i<deckRows.Length;i++) {
                var copy=i<equipped.Count?equipped[i]:null;
                deckRows[i].Bind(copy,copy==null?muted:Definition(copy).accent,copy!=null&&copy.Id==SelectedCopyId);
            }
            handSize.text=$"Рука  {Model.HandSize(WorkshopKind)} / {config.maximumHand}";
            bool maxHand=Model.HandSize(WorkshopKind)>=config.maximumHand;
            handUpgradeText.text=maxHand?"Рука улучшена":"+1 место   ·   "+Model.HandPrice(WorkshopKind);
            handUpgradeButton.interactable=!maxHand && Model.Coins>=Model.HandPrice(WorkshopKind);
            string departError=Model.CanDepart();departButton.interactable=departError==null;
            if(departError!=null && !embeddedMode)SetFeedback(departError,true);
            departText.text="В путь  →";
            RefreshDetail();
        }
        void RefreshDetail()
        {
            var copy=Market?null:Model.Find(SelectedCopyId);
            var definition=Market?config.Find(SelectedDefinitionId):Definition(copy);
            if(definition==null) {
                detailArt.enabled=false;detailTitle.text="Коллекция пуста";detailIdentity.text="Загляните в карточную лавку.";
                detailPower.text="";detailDescription.text="";equipText.text="Выберите карту";upgradeText.text="Улучшить";
                equipButton.interactable=false;upgradeButton.interactable=false;return;
            }
            detailArt.enabled=true;detailArt.sprite=definition.artwork;detailAccent.color=definition.accent;
            detailTitle.text=definition.displayName;
            detailIdentity.text=Market?KindName(definition.kind)+" / новая копия":$"№ {copy.Id:00}  ·  УРОВЕНЬ {copy.Level}";
            detailDescription.text=definition.description;
            detailPower.text="Сила  "+(copy?.Power??definition.basePower)+(copy!=null && copy.Level<config.maximumCardLevel?"  →  "+(copy.Power+definition.powerPerLevel):"");
            if(Market) {
                equipText.text="Купить  ·  "+definition.purchasePrice;equipButton.interactable=Model.Coins>=definition.purchasePrice;
                upgradeText.text="Сначала купите копию";upgradeButton.interactable=false;
            } else {
                equipText.text=Model.IsSelected(copy.Id)?"Убрать из колоды":"Взять в колоду";
                equipButton.interactable=Model.IsSelected(copy.Id)||Model.Selected(copy.Spec.Kind).Count<config.deckLimit;
                bool max=copy.Level>=config.maximumCardLevel;
                upgradeText.text=max?"Максимальный уровень":"Улучшить  ·  "+Model.UpgradePrice(copy);
                upgradeButton.interactable=!max && Model.Coins>=Model.UpgradePrice(copy);
            }
        }
        void TintTab(Button button,bool active)
        {
            var colors=button.colors; colors.normalColor=active?new Color(.28f,.34f,.28f):new Color(.13f,.17f,.15f);
            colors.highlightedColor=new Color(.33f,.38f,.31f);colors.selectedColor=colors.normalColor;button.colors=colors;
        }
        public void Depart()
        {
            if(embeddedMode || Busy)return;
            if(Departed==null){SetFeedback("Сцена вылазки не подключена.",true);return;}
            string error=Model.Depart(unchecked(Environment.TickCount*397+Model.Journeys));
            if(error!=null){SetFeedback(error,true);return;}
            presentation.enabled=false;
            control.SetBlocked(this,false);
            Sound(config.cardSound);
            Departed.Invoke();
        }
        public void CompleteOuting(string notice)
        {
            if(Model.IsAway)return;
            control.SetBlocked(this,true);
            presentation.enabled=true;
            RefreshWorkshop();SetFeedback(notice);Persist();
        }
    }
}

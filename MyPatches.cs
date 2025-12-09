using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Random = UnityEngine.Random;


namespace AutoDisplayCards;

[HarmonyPatch]
public class MyPatches
{
    //private static readonly List<int> SortedIndexList = new List<int>();
    //private static readonly List<float> SortedPriceList = new List<float>();
    private static bool _isRunning;
    private static readonly Dictionary<EItemType, WarehouseEntryList> WarehouseCache = [];
    private static readonly List<CardEntry> SortedCards = [];
    
    public static void FillCardShelves()
    {
        SortCards();
        if (SortedCards.Count == 0)
        {
            Plugin.LogDebugMessage("No applicable cards to fill shelves");
            return;
        }
        List<CardShelf> cardShelfList = CSingleton<ShelfManager>.Instance.m_CardShelfList;
        for (int i = 0; i < cardShelfList.Count; i++)
        {
            if (cardShelfList[i].m_ItemNotForSale)
            {
                Plugin.LogDebugMessage("Shelf " + i + " is a personal shelf");
                continue;
            }
            Plugin.LogDebugMessage("Filling shelf " + i);
            List<InteractableCardCompartment> cardCompartmentList = cardShelfList[i].GetCardCompartmentList();
            for (int j = 0; j < cardCompartmentList.Count; j++)
            {
                Plugin.LogDebugMessage("Filling compartment " + j);
                EFillResult result = FillCardSlot(cardCompartmentList[j]);
                switch (result)
                {
                    case EFillResult.Filled:
                        break;
                    case EFillResult.Full:
                        break;
                    case EFillResult.NoCards:
                        return;
                    default:
                        return;
                }
            }
        }
    }
    
    private static EFillResult FillCardSlot(InteractableCardCompartment comp)
    {
        if (comp.m_StoredCardList.Count == 0)
        {
            if (SortedCards.Count <= 0)
            {
                Plugin.LogDebugMessage("No more available cards");
                return EFillResult.NoCards;
            }
            int index = Plugin.RandomCardFill.Value ? Random.Range(0, SortedCards.Count) : 0;
            CardEntry entry = SortedCards[index];
            Plugin.LogDebugMessage("Filling card slot with card " + entry);
            InteractableCard3d card3d = CreateInteractableCard3d(ref entry);
            card3d.transform.position = comp.m_PutCardLocation.position;
            comp.SetCardOnShelf(card3d);
            CPlayerData.ReduceCardUsingIndex(entry.CardIndex, entry.ExpansionType, entry.Destiny, 1);
            if (entry.Amount <= 1)
            {
                Plugin.LogDebugMessage("Removed entry: " + entry);
                SortedCards.Remove(entry);
            }
            else
            {
                Plugin.LogDebugMessage("Lowering entry amount");
                SortedCards.RemoveAt(index);
                entry.Amount--;
                if(index > SortedCards.Count)SortedCards.Add(entry);
                else SortedCards.Insert(index, entry);
            }
            Plugin.LogDebugMessage("Finished reducing card and setting on shelf");
            return EFillResult.Filled;
        }
        else
        {
            InteractableCard3d debug = comp.m_StoredCardList.First();
            string monsterName = debug.m_Card3dUI.m_CardUI.GetCardData().monsterType.ToString();
            Plugin.LogDebugMessage("Card detected: " + monsterName);
            return EFillResult.Full;
        }
    }

    private static InteractableCard3d CreateInteractableCard3d(ref CardEntry entry)
    {
        Plugin.LogDebugMessage("Creating interactable card");
        Card3dUIGroup cardUI = CSingleton<Card3dUISpawner>.Instance.GetCardUI();
        InteractableCard3d card3d = ShelfManager.SpawnInteractableObject(EObjectType.Card3d).GetComponent<InteractableCard3d>();
        cardUI.m_IgnoreCulling = false;
        cardUI.m_CardUIAnimGrp.gameObject.SetActive(true);
        cardUI.m_CardUI.SetFoilCullListVisibility(true);
        cardUI.m_CardUI.ResetFarDistanceCull();
        cardUI.m_CardUI.SetFoilMaterialList(CSingleton<Card3dUISpawner>.Instance.m_FoilMaterialTangentView);
        cardUI.m_CardUI.SetFoilBlendedMaterialList(CSingleton<Card3dUISpawner>.Instance.m_FoilBlendedMaterialTangentView);
        cardUI.m_CardUI.SetCardUI(CPlayerData.GetCardData(entry.CardIndex, entry.ExpansionType, entry.Destiny));
        Plugin.LogDebugMessage("Got card data");
        card3d.SetCardUIFollow(cardUI);
        card3d.SetEnableCollision(false);
        return card3d;
    }

    public static Dictionary<EItemType, WarehouseEntryList> CacheWarehouse()
    {
        WarehouseCache.Clear();
        List<WarehouseShelf> wareShelfList = CSingleton<ShelfManager>.Instance.m_WarehouseShelfList;
        for (int i = 0; i < wareShelfList.Count; i++)
        {
            if(wareShelfList[i].GetIsBoxedUp()) continue;
            Plugin.LogDebugMessage("Warehouse shelf " + i);
            List<InteractableStorageCompartment> compList = wareShelfList[i].GetStorageCompartmentList();
            for (int j = 0; j < compList.Count; j++)
            {
                Plugin.LogDebugMessage("Compartment " + j);
                ShelfCompartment shelfCompartment = compList[j].GetShelfCompartment();
                List<InteractablePackagingBox_Item> boxList = shelfCompartment.GetInteractablePackagingBoxList();
                EItemType type = shelfCompartment.GetItemType();
                Plugin.LogDebugMessage("Found  " + boxList.Count + " boxes on shelf");
                int count = 0;
                foreach (InteractablePackagingBox_Item box in boxList)
                {
                    count += box.m_ItemCompartment.GetItemCount();
                }
                WarehouseEntry entry = new(i, j, count);
                if (WarehouseCache.ContainsKey(type))
                {
                    WarehouseCache[type].ContainsEntry(entry.ShelfIndex, entry.CompartmentIndex);
                    WarehouseCache[type].AddEntry(entry);
                }
                else
                {
                    WarehouseEntryList list = new();
                    list.AddEntry(entry);
                    WarehouseCache.Add(type, list);
                }
                Plugin.LogDebugMessage("Added entry " + type + ":" + entry);
            }
        }
        return WarehouseCache;
    }

    public static void FillItemShelves()
    {
        CacheWarehouse();
        if (Plugin.RefillSprayers.Value && Plugin.PrioritizeSprayersWhenRefillingStock.Value)
        {
            FillSprayers();
        }
        List<Shelf> shelfList = CSingleton<ShelfManager>.Instance.m_ShelfList;
        for (int i = 0; i < shelfList.Count; i++)
        {
            if (shelfList[i].m_ItemNotForSale)
            {
                Plugin.LogDebugMessage("Shelf " + i + " is a personal shelf");
                continue;
            }
            Plugin.LogDebugMessage("Filling shelf " + i);
            List<ShelfCompartment> compList = shelfList[i].GetItemCompartmentList();
            for (int j = 0; j < compList.Count; j++)
            {
                Plugin.LogDebugMessage("Filling compartment " + j);
                ShelfCompartment shelfCompartment = compList[j];
                if (shelfCompartment.GetItemCount() < shelfCompartment.GetMaxItemCount())
                {
                    FillItemShelf(shelfCompartment);
                }
                else Plugin.LogDebugMessage("Compartment already full");
            }
        }
        if (Plugin.RefillSprayers.Value && !Plugin.PrioritizeSprayersWhenRefillingStock.Value)
        {
            FillSprayers();
        }
    }

    private static void FillSprayers()
    {
        if (!WarehouseCache.ContainsKey(EItemType.Deodorant)) return;
        WarehouseEntryList list = WarehouseCache.GetValueSafe(EItemType.Deodorant);
        foreach (WarehouseEntry deodorant in list.Entries)
        {
            if (deodorant.Amount <= 0) continue;
            List<InteractableAutoCleanser> sprayers = ShelfManager.GetAutoCleanserList();
            ShelfCompartment wareComp = ShelfManager.Instance.m_WarehouseShelfList[deodorant.ShelfIndex].GetStorageCompartmentList()[deodorant.CompartmentIndex].GetShelfCompartment();
            foreach (InteractableAutoCleanser sprayer in sprayers)
            {
                while (sprayer.HasEnoughSlot())
                {
                    if (wareComp.GetItemCount() <= 0)break;
                    InteractablePackagingBox_Item box = wareComp.GetLastInteractablePackagingBox();
                    if (box is null) break;
                    Item firstItem = box.m_ItemCompartment.GetFirstItem();
                    firstItem.transform.position = sprayer.GetEmptySlotTransform().position;
                    firstItem.LerpToTransform(sprayer.GetEmptySlotTransform(), sprayer.GetEmptySlotTransform());
                    sprayer.AddItem(firstItem, true);
                    box.m_ItemCompartment.RemoveItem(firstItem);
                    if (box.m_ItemCompartment.GetItemCount() == 0)
                    {
                        CleanupBox(box, wareComp);
                    }
                }
                wareComp.SetPriceTagItemAmountText();
            }
        }
    }

    private static void FillItemShelf(ShelfCompartment shelfCompartment)
    {
        EItemType toFillType = shelfCompartment.GetItemType();
        Plugin.LogDebugMessage("Fill type: " + toFillType);
        if (!WarehouseCache.ContainsKey(toFillType)) return;
        WarehouseEntryList list = WarehouseCache.GetValueSafe(toFillType);
        foreach (WarehouseEntry entry in list.Entries)
        {
            if (entry.Amount <= 0) continue;
            ShelfCompartment wareComp = ShelfManager.Instance.m_WarehouseShelfList[entry.ShelfIndex].GetStorageCompartmentList()[entry.CompartmentIndex].GetShelfCompartment();
            if (!wareComp)
            {
                Plugin.LogDebugMessage("Warehouse comp null");
                break;
            }
            Plugin.LogDebugMessage("Warehouse Shelf: " + entry.ShelfIndex + " compartment: " + entry.CompartmentIndex);
            while (shelfCompartment.HasEnoughSlot())
            {
                Plugin.LogDebugMessage("Shelf has available slot");
                if (wareComp.GetItemCount() <= 0)
                {
                    Plugin.LogDebugMessage("Warehouse comp empty");
                    break;
                }
                InteractablePackagingBox_Item box = wareComp.GetLastInteractablePackagingBox();
                if (!box)
                {
                    Plugin.LogDebugMessage("Box is null");
                    break;
                }
                Plugin.LogDebugMessage("Box found");
                bool spawned = SpawnItemOnShelf(toFillType, shelfCompartment, box.m_ItemCompartment.GetFirstItem());
                if (!spawned)
                {
                    Plugin.LogDebugMessage($"Failed to spawn {toFillType} on shelf");
                    break;
                }
                Plugin.LogDebugMessage($"Spawned {toFillType} on shelf");
                RemoveItemFromBox(box, wareComp);
                Plugin.LogDebugMessage($"Removed {toFillType} from box");
                if (box.m_ItemCompartment.GetItemCount() == 0)
                {
                    CleanupBox(box, wareComp);
                    Plugin.LogDebugMessage("Box cleaned up");
                }
            }
            wareComp.SetPriceTagItemAmountText();
            Plugin.LogDebugMessage("Warehouse shelf empty or shelf full");
        }
        Plugin.LogDebugMessage("All warehouse shelves empty or shelf full");
    }

    public static bool SpawnItemOnShelf(EItemType toFillType, ShelfCompartment shelfCompartment, Item itemData)
    {
        if (toFillType == EItemType.None)
        {
            Plugin.LogDebugMessage("Item type is None");
            return false;
        }

        if (!shelfCompartment)
        {
            Plugin.LogDebugMessage("Shelf compartment is null");
            return false;
        }
        //ItemMeshData itemMeshData = InventoryBase.GetItemMeshData(toFillType);
        //Plugin.LogDebugMessage($"Got {toFillType} item mesh data");
        Item item = ItemSpawnManager.GetItem(shelfCompartment.m_StoredItemListGrp);
        Plugin.LogDebugMessage($"Spawned {toFillType} item");
        item.SetMesh(itemData.m_MeshFilter.mesh, itemData.m_Mesh.material, toFillType, 
            itemData.m_MeshFilterSecondary.mesh, itemData.m_MeshSecondary.material, itemData.m_Mesh.materials.ToList());
        Plugin.LogDebugMessage($"Set {toFillType} mesh");
        item.transform.position = shelfCompartment.GetEmptySlotTransform().position;
        Plugin.LogDebugMessage($"Set {toFillType} position");
        item.transform.localScale = shelfCompartment.GetEmptySlotTransform().localScale;
        Plugin.LogDebugMessage($"Set {toFillType} scale");
        item.transform.rotation = shelfCompartment.m_StartLoc.rotation;
        Plugin.LogDebugMessage($"Set {toFillType} rotation");
        item.gameObject.SetActive(true);
        Plugin.LogDebugMessage($"Set {toFillType} active");
        shelfCompartment.AddItem(item, false);
        Plugin.LogDebugMessage($"Added {toFillType} to shelf");
        return true;
    }

    public static void RemoveItemFromBox(InteractablePackagingBox_Item box, ShelfCompartment wareComp)
    {
        Item firstItem = box.m_ItemCompartment.GetFirstItem();
        firstItem.DisableItem();
        box.m_ItemCompartment.RemoveItem(firstItem);
        wareComp?.SetPriceTagItemAmountText();
    }

    public static void CleanupBox(InteractablePackagingBox_Item box, ShelfCompartment comp)
    {
        Plugin.LogDebugMessage("Starting box cleanup");
        RestockManager.RemoveItemPackageBox(box);
        Plugin.LogDebugMessage("Box removed from restock manager");
        if (comp is not null)
        {
            comp.RemoveBox(box);
            Plugin.LogDebugMessage("Box removed from shelf");
        }
        WorkerManager.Instance.m_TrashBin.DiscardBox(box, false);
        Plugin.LogDebugMessage("Box discarded to trash");
    }

    public static void MoveFloorBoxesToWarehouse()
    {
        CacheWarehouse();
        List<InteractablePackagingBox_Item> floorBoxes = RestockManager.Instance.m_ItemPackagingBoxList;
        floorBoxes.Sort((x, y) => y.m_ItemCompartment.GetItemCount().CompareTo(x.m_ItemCompartment.GetItemCount()));
        foreach (InteractablePackagingBox_Item box in floorBoxes)
        {
            MoveFloorBoxToWarehouse(box);
        }
    }

    public static bool MoveFloorBoxToWarehouse(InteractablePackagingBox_Item box)
    {
        if (box.m_IsStored || box.IsBoxOpened() || box.GetItemType() == EItemType.None ||
            !box.m_Collider.enabled || box.m_ItemCompartment.GetItemCount() <= 0 ||
            !WarehouseCache.ContainsKey(box.GetItemType()))
            return false;

        WarehouseEntryList list = WarehouseCache.GetValueSafe(box.GetItemType());

        foreach (WarehouseEntry entry in list.Entries)
        {
            List<InteractableStorageCompartment> compList = ShelfManager.Instance.m_WarehouseShelfList[entry.ShelfIndex].GetStorageCompartmentList();
            ShelfCompartment wareComp = compList[entry.CompartmentIndex].GetShelfCompartment();

            if (wareComp.GetInteractablePackagingBoxList().Count > 0 &&
                (wareComp.GetLastInteractablePackagingBox().m_IsBigBox != box.m_IsBigBox || !wareComp.HasEnoughSlot()) )
            {
                continue;
            }
            box.SetPhysicsEnabled(false);
            box.DispenseItem(false, wareComp);
            box.transform.position = wareComp.GetEmptySlotTransform().position;
            wareComp.ArrangeBoxItemBasedOnItemCount();
            wareComp.RefreshItemPosition(false);
            wareComp.RefreshPriceTagItemPriceText();
            return true;
        }
        return false;
    }

    public static List<CardEntry> SortCards()
    {
        SortedCards.Clear();
        List<ECardExpansionType> toSearch = [];
        if (Plugin.EnableMultiExpansion.Value)
        {
            foreach (ECardExpansionType type in Plugin.EnabledExpansions.Keys)
            {
                if(Plugin.EnabledExpansions[type].Value)toSearch.Add(type);
            }
        }
        else toSearch.Add(Plugin.FillCardExpansionType.Value);
        foreach (ECardExpansionType expansionType in toSearch){
            SortExpansionCards(expansionType);
            if (expansionType == ECardExpansionType.Ghost)
            {
                SortExpansionCards(expansionType,true);
            }
        }
        return SortedCards;
    }

    private static void SortExpansionCards(ECardExpansionType expansionType, bool isDestiny = false)
    {
        int saveIndex = 0;
        for (int i = 0; i < InventoryBase.GetShownMonsterList(expansionType).Count; i++)
        {
            Plugin.LogDebugMessage("Sorting monster list " + i);
            Plugin.LogDebugMessage("Cards for this monster: " + CPlayerData.GetCardAmountPerMonsterType(expansionType));
            for (int j = 0; j < CPlayerData.GetCardAmountPerMonsterType(expansionType); j++)
            {
                Plugin.LogDebugMessage("Checking card id " + saveIndex);
                CardData cardData = new()
                {
                    monsterType = CPlayerData.GetMonsterTypeFromCardSaveIndex(saveIndex, expansionType),
                    borderType = (ECardBorderType)(saveIndex % CPlayerData.GetCardAmountPerMonsterType(expansionType, false)),
                    isFoil = saveIndex % CPlayerData.GetCardAmountPerMonsterType(expansionType) >= CPlayerData.GetCardAmountPerMonsterType(expansionType, false),
                    isDestiny = isDestiny,
                    expansionType = expansionType
                };
                int cardAmount = CPlayerData.GetCardAmount(cardData);
                if (cardAmount <= Plugin.AmountToHold.Value)
                {
                    Plugin.LogDebugMessage("Not enough duplicates");
                    saveIndex++;
                    continue;
                }
                float marketPrice = 0;
                if (cardAmount > 0)
                {
                    marketPrice = CPlayerData.GetCardMarketPrice(cardData);
                }
                if ((marketPrice > Plugin.MaxCardValue.Value) || (marketPrice < Plugin.MinCardValue.Value))
                {
                    Plugin.LogDebugMessage("Card price not within range");
                    Plugin.LogDebugMessage("Price: " + marketPrice);
                    Plugin.LogDebugMessage("Max: " + Plugin.MaxCardValue.Value);
                    Plugin.LogDebugMessage("Min: " + Plugin.MinCardValue.Value);
                    saveIndex++;
                    continue;
                }
                CardEntry cardEntry = new(saveIndex, expansionType, marketPrice, isDestiny, cardAmount-Plugin.AmountToHold.Value);
                for (int n = 0; n < SortedCards.Count; n++)
                {
                    if (cardEntry.Price > SortedCards[n].Price)
                    {
                        Plugin.LogDebugMessage("Inserting card at position " + n);
                        SortedCards.Insert(n, cardEntry);
                        break;
                    }
                }
                if (!SortedCards.Contains(cardEntry))
                {
                    Plugin.LogDebugMessage("Adding card to list");
                    SortedCards.Add(cardEntry);
                }
                saveIndex++;
            }
        }
    }
    
    [HarmonyPatch(typeof(CGameManager), "Update")]
    [HarmonyPrefix]
    public static void GameManagerUpdatePrefix()
    {
        if (Plugin.FillCardTableKey.Value.IsDown() && !_isRunning && Plugin.PluginEnabled.Value)
        {
            Plugin.LogDebugMessage("Fill card table key detected");
            _isRunning = true;
            FillCardShelves();
            _isRunning = false;
        }
        if (Plugin.FillItemShelfKey.Value.IsDown() && !_isRunning && Plugin.PluginEnabled.Value)
        {
            Plugin.LogDebugMessage("Fill item shelf key detected");
            _isRunning = true;
            FillItemShelves();
            _isRunning = false;
        }
        if (Plugin.MoveFloorBoxesToShelvesKey.Value.IsDown() && !_isRunning && Plugin.PluginEnabled.Value)
        {
            Plugin.LogDebugMessage("Move floor boxes to shelves key detected");
            _isRunning = true;
            MoveFloorBoxesToWarehouse();
            _isRunning = false;
        }
    }

    public readonly struct WarehouseEntryList()
    {
        public WarehouseEntryList(List<WarehouseEntry> entries) : this()
        {
            this.Entries = entries;
        }

        public readonly List<WarehouseEntry> Entries = [];
        
        public void AddEntry(int shelfIndex, int compartmentIndex, int amount)
        {
            Entries.Add(new WarehouseEntry(shelfIndex,compartmentIndex,amount));
        }

        public void AddEntry(WarehouseEntry entry)
        {
            Entries.Add(entry);
        }

        public bool RemoveAt(int index)
        {
            if (Entries.Count - 1 >= index && index >= 0)
            {
                Entries.RemoveAt(index);
                return true;
            }
            return false;
        }

        public bool HasIndex(int index)
        {
            return Entries.Count - 1 >= index && index >= 0;
        }

        public bool ContainsEntry(int shelfIndex, int compartmentIndex)
        {
            foreach (WarehouseEntry entry in Entries)
            {
                if(entry.ShelfIndex == shelfIndex && entry.CompartmentIndex == compartmentIndex) return true;
            }
            return false;
        }

        public int GetIndexOfEntry(int shelfIndex, int compartmentIndex)
        {
            int index = 0; 
            foreach (WarehouseEntry entry in Entries)
            {
                if(entry.ShelfIndex == shelfIndex && entry.CompartmentIndex == compartmentIndex) return index;
                index++;
            }
            return -1;
        }

        public override string ToString() => $"(Entries:{Entries})";
    }
    
    public struct WarehouseEntry
    {
        public WarehouseEntry(int shelfIndex, int compartmentIndex, int amount)
        {
            ShelfIndex = shelfIndex;
            CompartmentIndex = compartmentIndex;
            Amount = amount;
        }

        public int ReduceAmount(int amount)
        {
            this.Amount -= amount;
            return this.Amount;
        }
        
        public int IncreaseAmount(int amount)
        {
            this.Amount += amount;
            return this.Amount;
        }
        
        public readonly int ShelfIndex = -1;
        public readonly int CompartmentIndex = -1;
        public int Amount = 0;
    }

    public struct CardEntry : IComparable
    {
        public CardEntry(int cardIndex, ECardExpansionType expansionType, float price, bool isDestiny, int amount)
        {
            this.CardIndex = cardIndex;
            this.ExpansionType = expansionType;
            this.Price = price;
            this.Destiny = isDestiny;
            this.Amount = amount;
        }
        
        public readonly int CardIndex = -1;
        public readonly ECardExpansionType ExpansionType = ECardExpansionType.None;
        public readonly float Price = 0;
        public readonly bool Destiny = false;
        public int Amount = 0;

        public readonly int CompareTo(object obj)
        {
            if (obj is not CardEntry other) return 1;
            if (this.Price > other.Price) return -1;
            if (this.Price < other.Price) return 1;
            return 0;
        }
        
        public readonly override string ToString()
            => $"(CardIndex:{CardIndex}, ExpansionType:{ExpansionType}, Price:{Price}, Destiny:{Destiny}, Amount:{Amount})";
    }

    public enum EFillResult
    {
        Filled,
        Full,
        NoCards
    }
}

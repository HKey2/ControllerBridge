#include "controller_profile.h"
#include "controller_bridge.h"
#include <string.h>

// Static scratch avoids using an entire core stack for a flash sector.
static up_bank scratch;
static int active_store=-1;
static bool load_bank(unsigned slot,up_bank *bank) {
    return ub_store_load(slot,bank,sizeof(*bank)) && up_bank_valid(bank);
}
bool up_registry_load(up_bank *bank) {
    bool first=load_bank(4,bank),second=load_bank(5,&scratch);
    active_store=-1;
    if(second && (!first || (int32_t)(scratch.generation-bank->generation)>0)) {
        *bank=scratch;active_store=5;return true;
    }
    if(first) { active_store=4;return true; }
    up_bank_clear(bank);return false;
}
bool up_registry_save(up_bank *bank,unsigned slot,const up_profile *profile) {
    return up_registry_save_identified(bank,slot,profile,NULL);
}
bool up_registry_save_identified(up_bank *bank,unsigned slot,const up_profile *profile,const uint8_t *id) {
    if(slot>=UP_PROFILE_COUNT || !up_bank_valid(bank) || !up_profile_valid(profile))return false;
    scratch=*bank;scratch.profiles[slot]=*profile;scratch.generation++;
    scratch.switch_keys[slot]=0;
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(up_same_controller(profile,&bank->profiles[i])) {
        scratch.switch_keys[slot]=bank->switch_keys[i];break;
    }
    if(!(profile->flags&UP_ENABLED))memset(scratch.profile_ids[slot],0,UP_ID_BYTES);
    else if(id)memcpy(scratch.profile_ids[slot],id,UP_ID_BYTES);
    up_prefer_slot(&scratch,slot);
    unsigned target=active_store==4?5:4;
    if(!ub_store_save(target,&scratch,sizeof(scratch),true))return false;
    *bank=scratch;active_store=(int)target;return true;
}
bool up_registry_switch_keys(up_bank *bank,unsigned slot,uint32_t keys) {
    if(slot>=UP_PROFILE_COUNT || !up_switch_keys_valid(keys) || !up_bank_valid(bank) || !(bank->profiles[slot].flags&UP_ENABLED))return false;
    if(bank->switch_keys[slot]==keys)return true;
    scratch=*bank;unsigned group=up_group_mask(bank,slot);
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i)if(group&(1u<<i))scratch.switch_keys[i]=keys;
    scratch.generation++;
    unsigned target=active_store==4?5:4;
    if(!ub_store_save(target,&scratch,sizeof(scratch),true))return false;
    *bank=scratch;active_store=(int)target;return true;
}
bool up_registry_select(up_bank *bank,unsigned slot) {
    if(slot>=UP_PROFILE_COUNT || !up_bank_valid(bank) || !(bank->profiles[slot].flags&UP_ENABLED))return false;
    scratch=*bank;up_prefer_slot(&scratch,slot);
    if(scratch.preferred_slots==bank->preferred_slots)return true;
    scratch.generation++;
    unsigned target=active_store==4?5:4;
    if(!ub_store_save(target,&scratch,sizeof(scratch),true))return false;
    *bank=scratch;active_store=(int)target;return true;
}
bool up_registry_replace(up_bank *bank,const up_bank *replacement) {
    if(!up_bank_valid(replacement))return false;
    const uint8_t empty[UP_ID_BYTES]={0};
    for(unsigned i=0;i<UP_PROFILE_COUNT;++i) {
        const uint8_t *id=replacement->profile_ids[i];
        if(!memcmp(id,empty,sizeof(empty)))continue; // An unset ID is permitted for an unassigned identity.
        if(!(replacement->profiles[i].flags&UP_ENABLED))return false;
        for(unsigned j=0;j<i;++j)
            if(!memcmp(id,replacement->profile_ids[j],UP_ID_BYTES))return false;
    }
    scratch=*replacement;scratch.generation=bank->generation+1;
    unsigned target=active_store==4?5:4;
    if(!ub_store_save(target,&scratch,sizeof(scratch),true))return false;
    *bank=scratch;active_store=(int)target;return true;
}

#include "egret_switch_profile.h"
#include <string.h>
void egret_default_configuration(ub_configuration *c) {
    memset(c,0,sizeof(*c)); c->version=UB_VERSION;
    c->input_interval_ms=1; c->output_interval_ms=1; c->warmup_ms=2000;
    // Measured physical order: top 9,4,1 / bottom 5,3,2; CREDIT7 START8 MENU10.
    c->button_map[8]=SW_Y; c->button_map[3]=SW_X; c->button_map[0]=SW_L;
    c->button_map[4]=SW_B; c->button_map[2]=SW_A; c->button_map[1]=SW_R;
    c->button_map[6]=SW_MINUS; c->button_map[7]=SW_PLUS; c->button_map[9]=SW_HOME;
}
bool egret_prepare_fields(const uc_hid_layout *layout,egret_fields *f) {
    f->has_ids=layout->has_report_ids;
    f->x=uc_hid_find(layout,1,0x30,0); f->y=uc_hid_find(layout,1,0x31,0);
    for(unsigned i=0;i<12;++i)f->buttons[i]=uc_hid_find(layout,9,i+1,0);
    return f->x && f->y;
}
bool egret_switch_report(const egret_fields *f,const uint8_t *raw,unsigned length,
                         const ub_configuration *c,sw_report *r) {
    int32_t x,y,value;
    if(!uc_hid_field_value(f->x,f->has_ids,raw,length,&x) || !uc_hid_field_value(f->y,f->has_ids,raw,length,&y))return false;
    sw_neutral(r); uint16_t buttons=0;
    for(unsigned i=0;i<12;++i)
        if(c->button_map[i] && uc_hid_field_value(f->buttons[i],f->has_ids,raw,length,&value) && value)buttons|=c->button_map[i];
    r->buttons0=buttons; r->buttons1=buttons>>8;
    int h=x<64?-1:x>192?1:0,v=y<64?-1:y>192?1:0;
    if(c->axis_mode!=1) {
        static const uint8_t hats[3][3]={{7,0,1},{6,15,2},{5,4,3}};
        r->hat=hats[v+1][h+1];
    }
    if(c->axis_mode) { r->lx=h<0?0:h>0?255:128; r->ly=v<0?0:v>0?255:128; }
    return true;
}

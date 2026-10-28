/* Protocol facts: knflrpn/2wiCC (2c2275c), progmem/Switch-Fightstick.
 * Pro HID descriptor derived from MIT-licensed 2wiCC usb_descriptors.h;
 * see licenses/2wicc-reference-MIT.txt. Command handling is project code.
 */
#include "switch_protocols.h"
#include <string.h>
static uint8_t device[18],configuration[80];
static const uint8_t nso_report[]={
    5,1,9,5,0xa1,1,0x85,5,5,0xff,9,1,0x15,0,0x26,0xff,0,0x95,63,0x75,8,0x81,2,
    0x85,10,9,1,0x95,2,0x81,2,5,9,0x19,1,0x29,21,0x25,1,0x95,21,0x75,1,0x81,2,
    0x95,1,0x75,3,0x81,3,5,1,9,1,0xa1,0,9,0x30,9,0x31,9,0x33,9,0x35,
    0x26,0xff,15,0x95,4,0x75,12,0x81,2,0xc0,5,0xff,9,2,0x26,0xff,0,
    0x95,52,0x75,8,0x81,2,0x85,3,9,1,0x95,63,0x91,2,0xc0
};
static const uint8_t hori_report[]={
    5,1,9,5,0xa1,1,0x15,0,0x25,1,0x35,0,0x45,1,0x75,1,0x95,16,
    5,9,0x19,1,0x29,16,0x81,2,5,1,0x25,7,0x46,0x3b,1,0x75,4,0x95,1,
    0x65,0x14,9,0x39,0x81,0x42,0x65,0,0x95,1,0x81,1,
    0x26,0xff,0,0x46,0xff,0,9,0x30,9,0x31,9,0x32,9,0x35,0x75,8,0x95,4,0x81,2,
    6,0,0xff,9,0x20,0x95,1,0x81,2,0x0a,0x21,0x26,0x95,8,0x91,2,0xc0
};
static const uint8_t pro_report[]={
    5,1,0x15,0,9,4,0xa1,1,0x85,0x30,5,1,5,9,0x19,1,0x29,10,0x15,0,0x25,1,
    0x75,1,0x95,10,0x55,0,0x65,0,0x81,2,5,9,0x19,11,0x29,14,0x15,0,0x25,1,
    0x75,1,0x95,4,0x81,2,0x75,1,0x95,2,0x81,3,
    0x0b,1,0,1,0,0xa1,0,0x0b,0x30,0,1,0,0x0b,0x31,0,1,0,
    0x0b,0x32,0,1,0,0x0b,0x35,0,1,0,0x15,0,0x27,0xff,0xff,0,0,
    0x75,16,0x95,4,0x81,2,0xc0,0x0b,0x39,0,1,0,0x15,0,0x25,7,
    0x35,0,0x46,0x3b,1,0x65,0x14,0x75,4,0x95,1,0x81,2,
    5,9,0x19,15,0x29,18,0x15,0,0x25,1,0x75,1,0x95,4,0x81,2,
    0x75,8,0x95,52,0x81,3,6,0,0xff,
    0x85,0x21,9,1,0x75,8,0x95,63,0x81,3,
    0x85,0x81,9,2,0x75,8,0x95,63,0x81,3,
    0x85,1,9,3,0x75,8,0x95,63,0x91,0x83,
    0x85,0x10,9,4,0x75,8,0x95,63,0x91,0x83,
    0x85,0x80,9,5,0x75,8,0x95,63,0x91,0x83,
    0x85,0x82,9,6,0x75,8,0x95,63,0x91,0x83,0xc0
};
void sp_descriptors(unsigned p,ud_options *o) {
    memcpy(device,sw_device_descriptor,18); memcpy(configuration,sw_configuration_descriptor,34);
    o->device=device; o->device_length=18; o->configuration=configuration; o->configuration_length=34;
    o->report=sw_report_descriptor; o->report_length=102; o->input_length=8;
    o->manufacturer="BDA"; o->product="NSW wired controller"; o->serial=NULL; o->output_endpoint=false;
    o->auxiliary_bulk=false; o->control_read=NULL;
    if(p==SP_POWERA_USB2) { device[2]=0; device[3]=2; }
    if(p==SP_POWERA_PACKET8)configuration[31]=8;
    if(p>=SP_HORI) {
        device[2]=0; device[3]=2; configuration[2]=41; configuration[13]=2;
        const uint8_t out[]={7,5,1,3,64,0,1}; memcpy(configuration+34,out,7);
        o->configuration_length=41; o->output_endpoint=true;
        if(p==SP_HORI) {
            device[8]=0x0d; device[9]=0x0f; device[10]=0x92; device[11]=0;
            o->report=hori_report; o->report_length=sizeof(hori_report);
            o->manufacturer="HORI CO.,LTD."; o->product="POKKEN CONTROLLER";
        } else {
            device[8]=0x7e; device[9]=5; device[10]=9; device[11]=0x20;
            device[12]=0; device[13]=2; device[16]=3; configuration[7]=0xa0;
            o->report=pro_report; o->report_length=sizeof(pro_report); o->input_length=64;
            o->manufacturer="Nintendo Co., Ltd."; o->product="Pro Controller"; o->serial="UC2PRO000001";
            configuration[33]=configuration[40]=p==SP_PRO8?8:1;
        }
        configuration[25]=o->report_length; configuration[26]=o->report_length>>8;
    }
    if(p>=SP_NSO4) {
        const uint8_t dev[]={18,1,0,2,0xef,2,1,64,0x7e,5,0x73,0x20,1,1,1,2,3,1};
        const uint8_t cfg[]={9,2,80,0,2,1,0,0xc0,0xfa,
            8,11,0,1,3,0,0,0,9,4,0,0,2,3,0,0,0,9,0x21,0x11,1,0,1,0x22,97,0,
            7,5,0x81,3,64,0,4,7,5,1,3,64,0,4,
            8,11,1,1,0xff,0,0,0,9,4,1,0,2,0xff,0,0,0,
            7,5,2,2,64,0,0,7,5,0x82,2,64,0,0};
        memcpy(device,dev,18); memcpy(configuration,cfg,80);
        configuration[41]=configuration[48]=p==SP_NSO4?4:1;
        o->configuration_length=80; o->report=nso_report; o->report_length=sizeof(nso_report);
        o->manufacturer="Nintendo"; o->product="Nintendo GameCube Controller"; o->serial="UC2NSO000001";
        o->auxiliary_bulk=true; o->control_read=sp_nso_control;
    }
}
static void stick(uint8_t *p,uint8_t x,uint8_t y) {
    unsigned a=x==128?2048:(unsigned)x*4095/255;
    unsigned b=y==128?2048:4095-(unsigned)y*4095/255;
    p[0]=a; p[1]=(a>>8)|((b&15)<<4); p[2]=b>>4;
}
void sp_encode(unsigned p,const sw_report *s,uint8_t r[64],uint32_t now) {
    memset(r,0,64);
    if(p<SP_PRO8) { memcpy(r,s,8); if(p==SP_HORI && r[2]==15)r[2]=8; return; }
    uint16_t b=s->buttons0|((uint16_t)s->buttons1<<8);
    if(p>=SP_NSO4) {
        r[0]=0x0a; r[1]=now/1000; r[2]=0x25;
        r[3]=((b&SW_B)?1:0)|((b&SW_A)?2:0)|((b&SW_Y)?4:0)|((b&SW_X)?8:0)|
             ((b&SW_R)?16:0)|((b&SW_ZR)?32:0)|((b&SW_PLUS)?64:0)|((b&SW_R3)?128:0);
        static const uint8_t hats2[]={8,10,2,3,1,5,4,12};
        r[4]=(s->hat<8?hats2[s->hat]:0)|((b&SW_L)?16:0)|((b&SW_ZL)?32:0)|((b&SW_MINUS)?64:0)|((b&SW_L3)?128:0);
        r[5]=((b&SW_HOME)?1:0)|((b&SW_CAPTURE)?2:0);
        stick(r+6,s->lx,s->ly); stick(r+9,s->rx,s->ry); r[12]=0x30;
        r[13]=(b&SW_ZL)?255:0; r[14]=(b&SW_ZR)?255:0; return;
    }
    r[0]=0x30; r[1]=now/1000; r[2]=0x91;
    r[3]=((b&SW_Y)?1:0)|((b&SW_X)?2:0)|((b&SW_B)?4:0)|((b&SW_A)?8:0)|
         ((b&SW_R)?64:0)|((b&SW_ZR)?128:0);
    r[4]=0x80|((b&SW_MINUS)?1:0)|((b&SW_PLUS)?2:0)|((b&SW_R3)?4:0)|
         ((b&SW_L3)?8:0)|((b&SW_HOME)?16:0)|((b&SW_CAPTURE)?32:0);
    static const uint8_t hats[8]={2,6,4,5,1,9,8,10};
    r[5]=(s->hat<8?hats[s->hat]:0)|((b&SW_L)?64:0)|((b&SW_ZL)?128:0);
    stick(r+6,s->lx,s->ly); stick(r+9,s->rx,s->ry); r[12]=9;
}
static uint8_t flash_byte(uint32_t address) {
    // Synthetic calibration for this bridge. No radio pairing keys are exposed.
    static const uint8_t sticks[18]={0,6,0x60,0,8,0x80,0,6,0x60,0,8,0x80,0,6,0x60,0,6,0x60};
    static const uint8_t imu[24]={0,0,0,0,0,0,0,0x40,0,0x40,0,0x40,0,0,0,0,0,0,0x3b,0x34,0x3b,0x34,0x3b,0x34};
    if(address==0x6012)return 3;
    if(address==0x601b)return 1;
    if(address>=0x6020 && address<0x6038)return imu[address-0x6020];
    if(address>=0x603d && address<0x604f)return sticks[address-0x603d];
    if(address>=0x6050 && address<0x605c)return 0x40;
    if(address>=0x6080 && address<0x60aa)return 0;
    return 0xff;
}
bool sp_command(sp_state *s,const uint8_t *c,unsigned n,const sw_report *input,uint8_t r[64],uint32_t now) {
    s->output_count++;
    if(n>=2 && c[0]==0x80) {
        memset(r,0,64); r[0]=0x81; r[1]=c[1];
        if(c[1]<32)s->handshake_mask|=1u<<c[1];
        if(c[1]==1) { r[3]=3; const uint8_t mac[6]={2,0,0,0x23,0x50,1}; memcpy(r+4,mac,6); }
        return true;
    }
    if(n<11 || c[0]!=1) { if(n && c[0]!=0x10)s->unknown_count++; return false; }
    uint8_t sub=c[10]; s->subcommand_mask|=1u<<(sub&31);
    sp_encode(SP_PRO1,input,r,now); r[0]=0x21; r[13]=0x80; r[14]=sub;
    if(sub==2) {
        r[13]=0x82; const uint8_t info[12]={3,0x48,3,2,2,0,0,0x23,0x50,1,1,1}; memcpy(r+15,info,12);
    } else if(sub==3 && n>=12)s->mode=c[11];
    else if(sub==0x10 && n>=16) {
        uint32_t address; memcpy(&address,c+11,4); unsigned length=c[15]; if(length>44)length=44;
        r[13]=0x90; memcpy(r+15,c+11,4); r[19]=length;
        for(unsigned i=0;i<length;++i)r[20+i]=flash_byte(address+i);
    } else if(sub==4)r[13]=0x83;
    else if(sub==0x21)r[13]=0xa0;
    else if(sub==0x50) { r[13]=0xd0; r[15]=0x90; r[16]=6; }
    return true;
}

// Switch 2 NSO protocol: independent implementation of documented packet facts.
// See ndeadly/switch2_controller_research and SoulCalDan's published FPGA USB
// capture responses. No firmware blobs/keys.
static uint8_t nso_memory(uint32_t address) {
    static const uint8_t calibration[9]={0,8,0x80,0,6,0x60,0,6,0x60};
    static const uint8_t parameters[40]={1,0xad,0xd9,0x9a,0x55,0x56,0x65,0xa0,0,10,0xa0,0,10,
        0xe2,0x20,0x0e,0xe2,0x20,0x0e,0x9a,0xad,0xd9,0x9a,0xad,0xd9,10,0xa5,0x50,10,0xa5,0x50,
        0x2f,0xf6,0x62,0x2f,0xf6,0x62,10,0xff,0xff};
    if(address>=0x130a8&&address<0x130b1)return calibration[address-0x130a8];
    if(address>=0x130e8&&address<0x130f1)return calibration[address-0x130e8];
    if(address>=0x13080&&address<0x130a8)return parameters[address-0x13080];
    if(address>=0x130c0&&address<0x130e8)return parameters[address-0x130c0];
    if(address==0x13140||address==0x13141)return 0;
    if(address>=0x13040&&address<0x13050)return 0;
    if(address>=0x13100&&address<0x13118)return 0;
    return 0xff;
}
int sp_nso_control(const uint8_t setup[8],uint8_t *r,uint16_t capacity,void *context) {
    (void)context; if(capacity<64)return -1;
    uint16_t value=setup[2]|(setup[3]<<8),index=setup[4]|(setup[5]<<8);
    memset(r,0,64);
    if(setup[0]==0x80&&setup[1]==6&&value==0x03ee) {
        r[0]=18;r[1]=3;const char name[]="MSFT100";
        for(unsigned i=0;i<7;++i)r[2+2*i]=name[i];
        r[16]=0x20; return 18;
    }
    if(setup[0]==0xc0&&setup[1]==0x20&&index==4) {
        r[0]=40;r[5]=1;r[6]=4;r[8]=1;r[16]=1;r[17]=1;memcpy(r+18,"WINUSB",6);return 40;
    }
    if((setup[0]==0xc0||setup[0]==0xc1)&&setup[1]==0x20&&index==5&&capacity>=142) {
        memset(r,0,142);r[0]=142;r[5]=1;r[6]=5;r[8]=1;r[10]=132;r[14]=1;r[18]=40;
        const char name[]="DeviceInterfaceGUID",guid[]="{F055869B-A439-4E70-9169-DC64616CF330}";
        for(unsigned i=0;i<19;++i)r[20+2*i]=name[i];
        r[60]=78;for(unsigned i=0;i<38;++i)r[64+2*i]=guid[i];return 142;
    }
    if(setup[0]==0xc0&&setup[1]==2&&value==0&&!index) {
        const uint8_t info[16]={1,1,2,0,0,0,12,0,0,0,2,0,0,0x23,0x50,2};memcpy(r,info,16);return 16;
    }
    if(setup[0]==0xc0&&setup[1]==3&&value==0&&!index) {
        memset(r,0xff,64);r[0]=1;r[1]=0;memcpy(r+2,"UC2NSO000000001",14);r[16]=r[17]=0;
        r[18]=0x7e;r[19]=5;r[20]=0x73;r[21]=0x20;r[22]=1;r[23]=4;r[24]=1;return 64;
    }
    return -1;
}
unsigned sp_nso_command(sp_state *s,const uint8_t *c,unsigned n,uint8_t r[128]) {
    if(n<8||c[1]!=0x91)return 0;
    s->output_count++; s->handshake_mask|=1u<<(c[0]&31); s->subcommand_mask|=1u<<(c[3]&31);
    memset(r,0,128);r[0]=c[0];r[1]=1;r[3]=c[3];r[5]=0xf8;
    uint8_t cmd=c[0],sub=c[3];
    if(cmd==3) {
        if(sub==0x0d) { s->active=true; if(!s->mode)s->mode=10; r[8]=1;return 12; }
        if(sub==3) { s->active=n>=12&&c[8]!=0;r[8]=s->active;return 12; }
        if(sub==10&&n>=12)s->mode=c[8];
        if(sub==15) { r[8]=5;return 12; }
        return 8;
    }
    if(cmd==2&&sub==4&&n>=16) {
        unsigned length=c[8];if(length>80)length=80;
        uint32_t address;memcpy(&address,c+12,4);r[8]=length;memcpy(r+12,c+12,4);
        for(unsigned i=0;i<length;++i)r[16+i]=nso_memory(address+i);
        return 16+length;
    }
    if(cmd==0x15) {
        r[8]=1;
        if(sub==1) { const uint8_t identity[8]={4,1,2,0,0,0x23,0x50,2};memcpy(r+9,identity,8);return 17; }
        if(sub==2) { for(unsigned i=0;i<16;++i)r[9+i]=0x50+i;return 25; }
        return 9;
    }
    if(cmd==0x10&&sub==1) { const uint8_t fw[12]={1,1,2,3,12,0,0,0,0xff,0xff,0xff,0xff};memcpy(r+8,fw,12);return 20; }
    if(cmd==7)return 9;
    if(cmd==0x16)return 29;
    if(cmd==0x0c)return 12;
    if(cmd==0x11) { r[8]=1;return sub==3?37:12; }
    if(cmd==9||cmd==10||cmd==11)return 8;
    s->unknown_count++;return 8;
}

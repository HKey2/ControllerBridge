#include "oled_text.h"
#include "controller_display.h"
#include <stddef.h>
#include <string.h>

// Locally drawn 5x7 strokes inside an 8x8 ASCII cell (one blank top row).
// Row bits run from left (bit 4) to right (bit 0).
static const uint8_t digits[10][7]={
 {14,17,19,21,25,17,14},{4,12,4,4,4,4,14},{14,17,1,2,4,8,31},
 {30,1,1,14,1,1,30},{2,6,10,18,31,2,2},{31,16,16,30,1,1,30},
 {14,16,16,30,17,17,14},{31,1,2,4,8,8,8},{14,17,17,14,17,17,14},
 {14,17,17,15,1,1,14}};
static const uint8_t upper[26][7]={
 {14,17,17,31,17,17,17},{30,17,17,30,17,17,30},{14,17,16,16,16,17,14},
 {30,17,17,17,17,17,30},{31,16,16,30,16,16,31},{31,16,16,30,16,16,16},
 {14,17,16,23,17,17,15},{17,17,17,31,17,17,17},{14,4,4,4,4,4,14},
 {7,2,2,2,18,18,12},{17,18,20,24,20,18,17},{16,16,16,16,16,16,31},
 {17,27,21,21,17,17,17},{17,25,21,19,17,17,17},{14,17,17,17,17,17,14},
 {30,17,17,30,16,16,16},{14,17,17,17,21,18,13},{30,17,17,30,20,18,17},
 {15,16,16,14,1,1,30},{31,4,4,4,4,4,4},{17,17,17,17,17,17,14},
 {17,17,17,17,17,10,4},{17,17,17,21,21,21,10},{17,17,10,4,10,17,17},
 {17,17,10,4,4,4,4},{31,1,2,4,8,16,31}};
static const uint8_t lower[26][7]={
 {0,0,14,1,15,17,15},{16,16,30,17,17,17,30},{0,0,14,16,16,17,14},
 {1,1,15,17,17,17,15},{0,0,14,17,31,16,14},{6,9,8,28,8,8,8},
 {0,15,17,17,15,1,14},{16,16,30,17,17,17,17},{4,0,12,4,4,4,14},
 {2,0,6,2,2,18,12},{16,16,18,20,24,20,18},{12,4,4,4,4,4,14},
 {0,0,26,21,21,21,21},{0,0,30,17,17,17,17},{0,0,14,17,17,17,14},
 {0,0,30,17,30,16,16},{0,0,15,17,15,1,1},{0,0,22,25,16,16,16},
 {0,0,15,16,14,1,30},{8,8,28,8,8,9,6},{0,0,17,17,17,19,13},
 {0,0,17,17,17,10,4},{0,0,17,17,21,21,10},{0,0,17,10,4,10,17},
 {0,0,17,17,15,1,14},{0,0,31,2,4,8,31}};
static const char punctuation[]="!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
static const uint8_t symbols[32][7]={
 {4,4,4,4,4,0,4},{10,10,10,0,0,0,0},{10,10,31,10,31,10,10},
 {4,15,20,14,5,30,4},{24,25,2,4,8,19,3},{12,18,20,8,21,18,13},
 {4,4,8,0,0,0,0},{2,4,8,8,8,4,2},{8,4,2,2,2,4,8},
 {0,21,14,31,14,21,0},{0,4,4,31,4,4,0},{0,0,0,0,4,4,8},
 {0,0,0,14,0,0,0},{0,0,0,0,0,4,4},{1,2,2,4,8,8,16},
 {0,4,4,0,4,4,0},{0,4,4,0,4,4,8},{1,2,4,8,4,2,1},
 {0,0,31,0,31,0,0},{16,8,4,2,4,8,16},{14,17,1,2,4,0,4},
 {14,17,23,21,23,16,14},{14,8,8,8,8,8,14},{16,8,8,4,2,2,1},
 {14,2,2,2,2,2,14},{4,10,17,0,0,0,0},{0,0,0,0,0,0,31},
 {8,4,2,0,0,0,0},{3,4,4,8,4,4,3},{4,4,4,4,4,4,4},
 {24,4,4,2,4,4,24},{0,0,9,22,0,0,0}};
static unsigned space_bytes(const unsigned char *p,unsigned n) {
    if(!n)return 0;
    if(*p==' ' || (*p>=9 && *p<=13))return 1;
    if(n>=2 && p[0]==0xc2 && (p[1]==0x85 || p[1]==0xa0))return 2;
    if(n>=3 && ((p[0]==0xe1 && p[1]==0x9a && p[2]==0x80) ||
       (p[0]==0xe2 && p[1]==0x80 && ((p[2]>=0x80 && p[2]<=0x8a) || p[2]==0xa8 || p[2]==0xa9 || p[2]==0xaf)) ||
       (p[0]==0xe2 && p[1]==0x81 && p[2]==0x9f) ||
       (p[0]==0xe3 && p[1]==0x80 && p[2]==0x80)))return 3;
    return 0;
}
static void period_text(char *row,uint32_t us) {
    memcpy(row,"--ms",4);
    if(!us)return;
    if(us<500) { memcpy(row,"<1ms",4);return; }
    unsigned ms=(us+500)/1000;
    if(ms<100) { row[0]=ms>=10?(char)('0'+ms/10):' ';row[1]=(char)('0'+ms%10);return; }
    unsigned tenths=(us+50000)/100000;
    if(tenths<100) { row[0]=(char)('0'+tenths/10);row[1]='.';row[2]=(char)('0'+tenths%10);row[3]='s'; }
    else memcpy(row,">=10",4);
}
void oled_text_format(char rows[4][9],uint8_t type,char mode,unsigned slot,const char *name,uint32_t period_us) {
    memset(rows,' ',36);for(unsigned row=0;row<4;++row)rows[row][8]=0;
    if(type==UDISPLAY_OFF || !udisplay_valid(type))return;
    char output=mode=='X'?'X':mode=='G'?'G':'S';
    if(type==UDISPLAY_NAME_PERIOD_SLOT) {
        period_text(rows[3],period_us);rows[3][5]=output;
        rows[3][6]=slot<16?(char)('0'+(slot+1)/10):'-';
        rows[3][7]=slot<16?(char)('0'+(slot+1)%10):'-';
    } else rows[3][7]=output;
    if(!name || slot>=16)return;
    const unsigned char *p=(const unsigned char *)name;
    unsigned n=0;while(n<48 && p[n])++n;
    if(type==UDISPLAY_NAME) {
        // Compact visible ASCII into 30 cells; normalize whitespace and wrap
        // long words. Non-ASCII bytes never consume the available cells.
        unsigned used=0;bool pending_space=false;
        for(unsigned i=0;i<n && used<30;) {
            unsigned ws=space_bytes(p+i,n-i);
            if(ws) { pending_space=used!=0;i+=ws;continue; }
            unsigned c=p[i++];if(c<0x21 || c>0x7e)continue;
            if(pending_space && used<30) { rows[used/8][used%8]=' ';++used; }
            pending_space=false;
            if(used<30) { rows[used/8][used%8]=(char)c;++used; }
        }
        return;
    }
    unsigned i=0,row=0;
    while(i<n && row<3) {
        unsigned ws;
        while(i<n && (ws=space_bytes(p+i,n-i)))i+=ws;
        if(i==n)break;
        unsigned column=0;
        while(i<n && !space_bytes(p+i,n-i)) {
            unsigned c=p[i++];
            if(column<8 && c>=0x21 && c<=0x7e)rows[row][column++]=(char)c;
        }
        ++row;
    }
}
void oled_text_detail_a(char rows[4][9],uint32_t in_us,uint32_t out_us,uint32_t bytes_per_second,uint32_t device_ids) {
    memset(rows,' ',36);for(unsigned row=0;row<4;++row)rows[row][8]=0;
    memcpy(rows[1]," IN:",4);period_text(rows[1]+4,in_us);
    memcpy(rows[2],"OUT:",4);period_text(rows[2]+4,out_us);
    // Seven decimal digits plus B fit the panel. Full-speed USB is well below
    // this bound; saturate defensively instead of wrapping or overrunning.
    unsigned value=bytes_per_second>9999999?9999999:bytes_per_second;
    char digits[7];unsigned count=0;
    do { digits[count++]=(char)('0'+value%10);value/=10; } while(value);
    for(unsigned i=0;i<count;++i)rows[3][i]=digits[count-1-i];
    rows[3][count]='B';
    static const char hex[]="0123456789ABCDEF";
    for(unsigned i=0;i<8;++i)rows[0][i]=device_ids?hex[(device_ids>>(28-4*i))&15]:'-';
}
static void hex_text(char *text,uint32_t value,unsigned digits) {
    static const char hex[]="0123456789ABCDEF";
    for(unsigned i=0;i<digits;++i)text[i]=hex[(value>>(4*(digits-1-i)))&15];
}
void oled_text_detail_b(char rows[4][9],uint32_t device_ids,const udisplay_input *state) {
    memset(rows,'-',36);for(unsigned row=0;row<4;++row)rows[row][8]=0;
    if(device_ids)hex_text(rows[0],device_ids,8);
    if(!device_ids || !state)return;
    if(state->buttons_valid)hex_text(rows[1],state->buttons,8);
    for(unsigned axis=0;axis<4;++axis)if(state->valid_axes&(1u<<axis)) {
        char *cell=rows[2+axis/2]+4*(axis%2);
        if(state->wide_axes&(1u<<axis))memset(cell,'*',4);
        else hex_text(cell,state->axes[axis],4);
    }
}
// Six visible cells per name leave room for both brackets. IDs match the GUI's
// saved presentation choices, including former None=0 as Switch. Four private
// glyphs retain the PlayStation symbols without a general Unicode font.
enum { N_EMPTY,N_Y,N_B,N_A,N_X,N_L,N_R,N_ZL,N_ZR,N_MINUS,N_PLUS,N_LSTICK,N_RSTICK,N_HOME,N_CAPTURE,
    N_LB,N_RB,N_LT,N_RT,N_BACK,N_START,N_GUIDE,N_SQUARE,N_CROSS,N_CIRCLE,N_TRIANGLE,N_L1,N_R1,N_L2,N_R2,
    N_SELECT,N_L3,N_R3,N_PS,N_SHARE,N_OPTION,N_C,N_Z,N_MODE,N_D,N_RUN,N_III,N_I,N_II,N_V,N_UP,N_DOWN,N_LEFT,N_RIGHT };
static const char button_names[][7]={
    "","Y","B","A","X","L","R","ZL","ZR","-","+","LStick","RStick","Home","Capt.",
    "LB","RB","LT","RT","BACK","START","GUIDE","\x01","\x02","\x03","\x04","L1","R1","L2","R2",
    "SELECT","L3","R3","PS","SHARE","Option","C","Z","MODE","D","RUN","III","I","II","V","Up","Down","Left","Right"};
static const uint8_t button_systems[14][14]={
    {N_Y,N_B,N_A,N_X,N_L,N_R,N_ZL,N_ZR,N_MINUS,N_PLUS,N_LSTICK,N_RSTICK,N_HOME,N_CAPTURE},
    {N_Y,N_B,N_A,N_X,N_L,N_R,N_ZL,N_ZR,N_MINUS,N_PLUS,N_LSTICK,N_RSTICK,N_HOME,N_CAPTURE},
    {N_X,N_A,N_B,N_Y,N_LB,N_RB,N_LT,N_RT,N_BACK,N_START,N_LSTICK,N_RSTICK,N_GUIDE,N_EMPTY},
    {N_SQUARE,N_CROSS,N_CIRCLE,N_TRIANGLE,N_L1,N_R1,N_L2,N_R2,N_SHARE,N_OPTION,N_L3,N_R3,N_PS,N_EMPTY},
    {N_SQUARE,N_CROSS,N_CIRCLE,N_TRIANGLE,N_L1,N_R1,N_L2,N_R2,N_SELECT,N_START,N_L3,N_R3,N_HOME,N_EMPTY},
    {N_A,N_B,N_C,N_Y,N_X,N_Z,N_L,N_R,N_EMPTY,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_X,N_A,N_B,N_Y,N_C,N_Z,N_L,N_R,N_EMPTY,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_A,N_B,N_C,N_Y,N_X,N_Z,N_EMPTY,N_EMPTY,N_MODE,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_X,N_A,N_B,N_Y,N_C,N_Z,N_EMPTY,N_EMPTY,N_MODE,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_Y,N_B,N_A,N_X,N_EMPTY,N_EMPTY,N_L,N_R,N_SELECT,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_A,N_B,N_D,N_C,N_EMPTY,N_EMPTY,N_EMPTY,N_EMPTY,N_SELECT,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_EMPTY,N_B,N_A,N_EMPTY,N_EMPTY,N_EMPTY,N_EMPTY,N_EMPTY,N_SELECT,N_START,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_III,N_I,N_II,N_V,N_EMPTY,N_EMPTY,N_EMPTY,N_EMPTY,N_SELECT,N_RUN,N_EMPTY,N_EMPTY,N_HOME,N_EMPTY},
    {N_SQUARE,N_CROSS,N_CIRCLE,N_TRIANGLE,N_L1,N_R1,N_L2,N_R2,N_SELECT,N_START,N_L3,N_R3,N_PS,N_EMPTY}
};
static void button_text(char row[9],uint8_t system,unsigned button) {
    if(system>=14)system=1;
    const char *name=button_names[button<14?button_systems[system][button]:N_UP+button-14];
    char fallback[6]={'B','t','n',(char)('0'+(button+1)/10),(char)('0'+(button+1)%10),0};
    if(!*name)name=fallback;
    row[0]='[';unsigned n=0;while(n<6 && name[n]) { row[n+1]=name[n];++n; }row[n+1]=']';
}
void oled_text_send_info(char rows[4][9],unsigned slot,const char *name,uint8_t labels,const udisplay_output *state) {
    // Reuse the existing ASCII/whitespace handling, retaining its first row.
    oled_text_format(rows,UDISPLAY_NAME,'S',slot,name,0);
    for(unsigned row=1;row<4;++row)memset(rows[row],' ',8);
    if(!state) { for(unsigned row=1;row<4;++row)memset(rows[row],'-',8);return; }
    unsigned row=1;
    for(unsigned button=0;button<18 && row<3;++button)
        if(state->buttons&(1u<<button))button_text(rows[row++],labels,button);
    // Left has a stable priority when both sticks are active. At neutral both
    // pairs have the same displayed value, so no last-motion history is needed.
    unsigned axis=(state->axes[0] || state->axes[1])?0:(state->axes[2] || state->axes[3])?2:0;
    hex_text(rows[3],state->axes[axis],4);hex_text(rows[3]+4,state->axes[axis+1],4);
}
static void pointer_axis(char row[9],char axis,int32_t value,bool valid) {
    row[0]=axis;
    if(!valid) { memset(row+1,'-',7);return; }
    row[1]=value<0?'-':'+';
    uint32_t magnitude=value<0?0u-(uint32_t)value:(uint32_t)value;
    if(magnitude>999999) { memset(row+2,'*',6);return; }
    for(unsigned i=8;i-->2;) { row[i]=(char)('0'+magnitude%10);magnitude/=10; }
}
static void report_rate(char row[9],char label,uint32_t rate,bool valid) {
    memcpy(row,"  ----/s",8);row[0]=label;
    if(!valid)return;
    if(rate>9999) { memcpy(row+1,">9999/s",7);return; }
    memset(row+2,' ',4);unsigned column=6;
    do { row[--column]=(char)('0'+rate%10);rate/=10; } while(rate);
}
void oled_text_detail_c(char rows[4][9],bool connected,int32_t x,int32_t y,bool motion_valid,uint32_t limit,uint32_t reports,bool reports_valid) {
    memset(rows,'-',36);for(unsigned row=0;row<4;++row)rows[row][8]=0;
    if(!connected)return;
    pointer_axis(rows[0],'X',x,motion_valid);pointer_axis(rows[1],'Y',y,motion_valid);
    report_rate(rows[2],'R',limit,limit!=0);
    report_rate(rows[3],'S',reports,reports_valid);
}
static const uint8_t button_symbols[4][7]={
    {31,17,17,17,17,17,31}, {17,17,10,4,10,17,17},
    {14,17,17,17,17,17,14}, {4,4,10,10,17,17,31}
};
void oled_text_render(uint8_t pixels[256],const char rows[4][9]) {
    memset(pixels,0,256);
    for(unsigned row=0;row<4;++row)for(unsigned column=0;column<8 && rows[row][column];++column) {
        unsigned char c=(unsigned char)rows[row][column];
        const uint8_t *glyph=c>=1&&c<=4?button_symbols[c-1]:c>='0'&&c<='9'?digits[c-'0']:c>='A'&&c<='Z'?upper[c-'A']:
            c>='a'&&c<='z'?lower[c-'a']:NULL;
        if(!glyph && c!=' ') {
            const char *p=strchr(punctuation,c);
            if(p)glyph=symbols[p-punctuation];
        }
        if(!glyph)continue;
        for(unsigned x=0;x<5;++x)for(unsigned y=0;y<7;++y)
            if(glyph[y]&(16u>>x))pixels[row*64+column*8+1+x]|=(uint8_t)(1u<<(y+1));
    }
}

// Direct-root bus maintenance while XIP is unavailable. No endpoint transfers,
// callbacks, connection discovery, or PIO instruction-table reads in this path.
static bool flash_frame_active;
void uc_pio_flash_prepare(void) {
  root_port_t *root=PIO_USB_ROOT_PORT(0);
  pio_port_t *pp=PIO_USB_PIO_PORT(0);
  flash_frame_active=timer_active && root->initialized && root->connected && !root->suspended;
  if(!flash_frame_active)return;
  configure_root_port(pp,root);
  // Direct-root only: exclude the hub PRE path from the SRAM-only loop.
  pp->need_pre=false;
}
void __not_in_flash_func(uc_pio_flash_frame)(void) {
  if(!flash_frame_active)return;
  root_port_t *root=PIO_USB_ROOT_PORT(0);
  pio_port_t *pp=PIO_USB_PIO_PORT(0);
  if(root->is_fullspeed)
    pio_usb_bus_usb_transfer(pp,sof_packet_encoded,sof_packet_encoded_len);
  else
    pio_usb_bus_usb_transfer(pp,keepalive_encoded,1);
  ++sof_count;
  uint16_t frame=sof_count&0x7ff;
  sof_packet[2]=(uint8_t)frame;
  sof_packet[3]=(calc_usb_crc5(frame)<<3)|(frame>>8);
  sof_packet_encoded_len=pio_usb_ll_encode_tx_data(sof_packet,sizeof(sof_packet),sof_packet_encoded);
}

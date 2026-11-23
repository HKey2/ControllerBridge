// Direct-root Low Speed HID input. Enumeration has already selected the bus
// clock and checked the endpoint layout. No hubs, OUT/SETUP transactions,
// per-frame speed reconfiguration, or SOF encoding on this active path.
static endpoint_t *low_speed_inputs[2];
static unsigned low_speed_input_count;
bool uc_pio_lowspeed_begin(void) {
  root_port_t *root=PIO_USB_ROOT_PORT(0);
  if(root->is_fullspeed || !root->connected || root->suspended)return false;
  low_speed_input_count=0;
  for(unsigned i=0;i<PIO_USB_EP_POOL_CNT;++i) {
    endpoint_t *ep=PIO_USB_ENDPOINT(i);
    if(!ep->size || ep->root_idx || ep->dev_addr!=1 || !(ep->ep_num&127))continue;
    if(!(ep->ep_num&EP_IN) || (ep->attr&3)!=EP_ATTR_INTERRUPT || ep->size>8 ||
       ep->need_pre || !ep->interval || low_speed_input_count==2)return false;
    low_speed_inputs[low_speed_input_count++]=ep;
  }
  configure_lowspeed_host(PIO_USB_PIO_PORT(0),root);
  return low_speed_input_count!=0;
}
void __not_in_flash_func(uc_pio_lowspeed_frame)(void) {
  root_port_t *root=PIO_USB_ROOT_PORT(0);
  if(!timer_active || !root->initialized || !root->connected || root->suspended)return;
  if(connection_check(root)) {
    pio_port_t *pp=PIO_USB_PIO_PORT(0);
    pio_usb_bus_usb_transfer(pp,keepalive_encoded,1);
    for(unsigned i=0;i<low_speed_input_count;++i) {
      endpoint_t *ep=low_speed_inputs[i];
      if(ep->interval_counter) { --ep->interval_counter;continue; }
      if(!ep->has_transfer || ep->transfer_aborted)continue;
      ep->transfer_started=true;
      usb_in_transaction(pp,ep);
      ep->interval_counter=ep->interval-1;
      ep->transfer_started=false;
    }
    ++sof_count;
  }
  if(root->ints)pio_usb_host_irq_handler(0);
}

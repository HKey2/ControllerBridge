// A single OUT transaction between input passes. This path is selected only
// for an enabled feedback pipe; the original frame scheduler remains intact.
bool __not_in_flash_func(uc_pio_feedback_slice)(uint8_t address) {
  root_port_t *root=PIO_USB_ROOT_PORT(0);
  endpoint_t *ep=_find_ep(0,1,address==0x80?0:address);
  if(!root->connected || root->suspended || !root->is_fullspeed || !ep ||
     !ep->has_transfer || ep->transfer_aborted || ep->need_pre || ep->interval_counter)return false;
  pio_port_t *pp=PIO_USB_PIO_PORT(0);
  configure_root_port(pp,root);
  ep->transfer_started=true;
  if(address==0x80 && ep->data_id==USB_PID_SETUP)usb_setup_transaction(pp,ep);
  else if(address==0x80 && (ep->ep_num&EP_IN))usb_in_transaction(pp,ep);
  else usb_out_transaction(pp,ep);
  ep->interval_counter=ep->interval?ep->interval-1:0;
  ep->transfer_started=false;
  if(root->ints)pio_usb_host_irq_handler(0);
  return true;
}

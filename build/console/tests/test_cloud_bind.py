"""Cloud launch must honor a loopback-only bind without changing LAN defaults."""
import os
from unittest import TestCase
from unittest.mock import Mock, patch
from console_app import config
from console_app.remote_server import RemoteServer

class CloudBindTests(TestCase):
    def test_explicit_cloud_bind_is_loopback_and_reports_its_address(self):
        server=RemoteServer(Mock(),lambda:dict(config.DEFAULT_CONFIG),lambda value:None)
        try:
            with patch.dict(os.environ,{'WLC_BIND_HOST':'127.0.0.1'}):
                self.assertTrue(server.start(0)[0])
            self.assertEqual(server.httpd.server_address[0],'127.0.0.1')
            self.assertEqual(server.state.bind_host,'127.0.0.1')
        finally:server.stop()

    def test_invalid_bind_cannot_become_a_hostname_or_external_url(self):
        for value in ('https://example.com','example.com','224.0.0.1'):
            server=RemoteServer(Mock(),lambda:dict(config.DEFAULT_CONFIG),lambda value:None)
            with patch.dict(os.environ,{'WLC_BIND_HOST':value}):
                self.assertFalse(server.start(0)[0])
            self.assertIsNone(server.httpd)

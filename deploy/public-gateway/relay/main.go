// A bounded, byte-transparent Unix-to-loopback relay. Docker binds this process
// to one immutable target network namespace; it never follows a host port or IP.
package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"runtime"
	"runtime/debug"
	"strconv"
	"sync"
	"syscall"
	"time"
)

const maxLease = 10 * time.Second
const bufferSize = 16 * 1024

var identityPattern = regexp.MustCompile(`^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$`)

type lease struct {
	PublicationID string `json:"publicationId"`
	ExpiresAt     int64  `json:"expiresAtUnixMs"`
}

func readSmallFile(path string, limit int64) ([]byte, error) {
	// Reject a FIFO without blocking in open() before we can inspect its mode.
	file, err := os.OpenFile(path, os.O_RDONLY|syscall.O_NOFOLLOW|syscall.O_NONBLOCK, 0)
	if err != nil {
		return nil, errors.New("state_file_unavailable")
	}
	defer file.Close()
	info, err := file.Stat()
	if err != nil || !info.Mode().IsRegular() || info.Size() > limit {
		return nil, errors.New("state_file_invalid")
	}
	raw, err := io.ReadAll(io.LimitReader(file, limit+1))
	if err != nil || int64(len(raw)) > limit {
		return nil, errors.New("state_file_invalid")
	}
	return raw, nil
}

func readLease(root, identity string) ([]byte, time.Duration, error) {
	raw, err := readSmallFile(filepath.Join(root, "lease.json"), 512)
	var value lease
	if err != nil || len(raw) > 512 || json.Unmarshal(raw, &value) != nil || value.PublicationID != identity {
		return nil, 0, errors.New("lease_invalid")
	}
	remaining := time.Until(time.UnixMilli(value.ExpiresAt))
	if remaining <= 0 || remaining > maxLease {
		return nil, 0, errors.New("lease_expired_or_unbounded")
	}
	return raw, remaining, nil
}

type relay struct {
	ctx       context.Context
	mu        sync.Mutex
	closing   bool
	active    map[net.Conn]struct{}
	listeners []*net.UnixListener
	slots     chan struct{}
	wg        sync.WaitGroup
}

func (r *relay) track(connections ...net.Conn) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closing {
		return false
	}
	for _, connection := range connections {
		r.active[connection] = struct{}{}
	}
	return true
}

func (r *relay) close() {
	r.mu.Lock()
	r.closing = true
	for _, listener := range r.listeners {
		listener.Close()
	}
	for connection := range r.active {
		connection.Close()
	}
	r.mu.Unlock()
	r.wg.Wait()
}

func (r *relay) accept(listener *net.UnixListener, port int) {
	defer r.wg.Done()
	for {
		incoming, err := listener.AcceptUnix()
		if err != nil {
			return
		}
		select {
		case r.slots <- struct{}{}:
			if !r.track(incoming) {
				incoming.Close()
				<-r.slots
				return
			}
			r.wg.Add(1)
			go r.forward(incoming, port)
		default:
			incoming.Close()
		}
	}
}

func (r *relay) forward(incoming *net.UnixConn, port int) {
	defer r.wg.Done()
	defer func() {
		incoming.Close()
		r.mu.Lock()
		delete(r.active, incoming)
		r.mu.Unlock()
		<-r.slots
	}()
	dialer := net.Dialer{Timeout: 2 * time.Second}
	connection, err := dialer.DialContext(r.ctx, "tcp4", net.JoinHostPort("127.0.0.1", strconv.Itoa(port)))
	if err != nil {
		return
	}
	upstream := connection.(*net.TCPConn)
	defer upstream.Close()
	if !r.track(upstream) {
		return
	}
	defer func() { r.mu.Lock(); delete(r.active, upstream); r.mu.Unlock() }()
	complete := make(chan struct{}, 1)
	go func() {
		_, err := io.CopyBuffer(struct{ io.Writer }{upstream}, struct{ io.Reader }{incoming}, make([]byte, bufferSize))
		if err != nil {
			upstream.Close()
			incoming.Close()
		} else {
			upstream.CloseWrite()
		}
		complete <- struct{}{}
	}()
	_, err = io.CopyBuffer(struct{ io.Writer }{incoming}, struct{ io.Reader }{upstream}, make([]byte, bufferSize))
	if err != nil {
		upstream.Close()
		incoming.Close()
	} else {
		incoming.CloseWrite()
	}
	<-complete
}

func run(root, leaseRoot, identity string, maximum int, ports []int) error {
	last, remaining, err := readLease(leaseRoot, identity)
	if err != nil {
		return err
	}
	// A publication is single-use, including after manual docker start or daemon restart.
	marker, err := os.OpenFile(filepath.Join(root, "started"), os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return errors.New("publication_already_started")
	}
	marker.Close()
	ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGTERM, syscall.SIGINT)
	defer cancel()
	r := relay{ctx: ctx, active: make(map[net.Conn]struct{}), slots: make(chan struct{}, maximum)}
	defer r.close()
	for _, port := range ports {
		path := filepath.Join(root, strconv.Itoa(port)+".sock")
		// Never unlink an existing socket. A new publication must have a fresh private directory.
		listener, err := net.ListenUnix("unix", &net.UnixAddr{Name: path, Net: "unix"})
		if err != nil {
			return errors.New("listener_unavailable")
		}
		r.listeners = append(r.listeners, listener)
		if os.Chmod(path, 0600) != nil {
			return errors.New("socket_permissions_failed")
		}
	}
	for index, listener := range r.listeners {
		r.wg.Add(1)
		go r.accept(listener, ports[index])
	}
	if os.WriteFile(filepath.Join(root, "ready"), []byte(identity), 0600) != nil {
		return errors.New("ready_marker_failed")
	}
	defer os.Remove(filepath.Join(root, "ready"))
	// time.Now.Add carries a monotonic clock. An unchanged file cannot extend the lease.
	deadline := time.Now().Add(remaining)
	ticker := time.NewTicker(100 * time.Millisecond)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return nil
		case <-ticker.C:
			if !time.Now().Before(deadline) {
				cancel()
				return errors.New("lease_expired")
			}
			current, remaining, err := readLease(leaseRoot, identity)
			if err != nil {
				cancel()
				return err
			}
			if string(current) != string(last) {
				deadline = time.Now().Add(remaining)
				last = current
			}
		}
	}
}

func main() {
	runtime.GOMAXPROCS(1)
	debug.SetMemoryLimit(10 << 20)
	if len(os.Args) < 4 || !filepath.IsAbs(os.Args[2]) || len(os.Args[2]) > 70 || !filepath.IsAbs(os.Args[3]) || len(os.Args[3]) > 256 {
		fmt.Fprintln(os.Stderr, "invalid_arguments")
		os.Exit(2)
	}
	root, leaseRoot := filepath.Clean(os.Args[2]), filepath.Clean(os.Args[3])
	if os.Args[1] == "health" {
		ready, err := readSmallFile(filepath.Join(root, "ready"), 64)
		identity := string(ready)
		_, _, leaseErr := readLease(leaseRoot, identity)
		if err != nil || !identityPattern.MatchString(identity) || leaseErr != nil {
			os.Exit(1)
		}
		return
	}
	if os.Args[1] != "run" || len(os.Args) < 7 || len(os.Args) > 70 || !identityPattern.MatchString(os.Args[4]) {
		os.Exit(2)
	}
	identity := os.Args[4]
	maximum, err := strconv.Atoi(os.Args[5])
	if err != nil || maximum < 1 || maximum > 64 {
		os.Exit(2)
	}
	seen := make(map[int]bool)
	var ports []int
	for _, value := range os.Args[6:] {
		port, err := strconv.Atoi(value)
		if err != nil || port < 1 || port > 65535 || seen[port] {
			os.Exit(2)
		}
		seen[port] = true
		ports = append(ports, port)
	}
	if err := run(root, leaseRoot, identity, maximum, ports); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
